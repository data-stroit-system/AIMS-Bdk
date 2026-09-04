#!/usr/bin/env bash
#
# Connection-string-only Oracle backup/restore for AIMS.
#
# Unlike db_backup.sh (Data Pump via podman container / native DBA access),
# this script needs NOTHING but a working sqlplus and a plain user/password
# connection string: no expdp/impdp, no local Oracle home, no OS
# authentication, no DIRECTORY objects. Works against a remote Oracle too,
# as long as the connected user owns the schema being dumped (it exports
# that user's own tables).
#
#   backup  = schema DDL (dbms_metadata: tables incl. constraints, indexes,
#             sequences, triggers) + full data as INSERT statements, in one
#             .sql file.
#   restore = drops the connected user's tables (CASCADE CONSTRAINTS),
#             replays the backup file, then re-bumps every sequence above
#             MAX(Id) so the app's NEXTVAL can never collide with restored
#             rows (the app assigns Id via BEFORE-INSERT triggers that read
#             <Table>_SEQ; explicit-Id INSERTs bypass them).
#
# Requirements / limits (all fine for AIMS as of 2026-08):
#   - Oracle 11gR2+ (uses LISTAGG, q-quoting, DBMS_METADATA, DBMS_LOB).
#     sqlplus from the Instant Client is fine.
#   - Only single-level FK chains are ordered (parents before children).
#   - CLOB/NCLOB values must fit 15,000 chars; the backup fails
#     loudly instead of truncating. No LONG/BLOB/BFILE columns.
#   - Restore targets the same database instance the backup came from (the
#     DDL carries TABLESPACE "USERS" etc.).
#
# Usage (from the repo root):
#   ./deploy/db_backup_sqlplus.sh backup [connection-string]
#   ./deploy/db_backup_sqlplus.sh restore <backup.sql> [--yes]
#   ./deploy/db_backup_sqlplus.sh list
#
# connection-string: 'user/password@//host:port/service'
# Also honoured: DB_CONNECTION_STRING env var, or fall back to reading the
# Oracle string out of $APPSETTINGS_FILE (default
# $BASE_DIR/shared/appsettings.Production.json, override in deploy.conf --
# same python3 parse as db_backup.sh).
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONF_FILE="$SCRIPT_DIR/deploy.conf"

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

[[ -f "$CONF_FILE" ]] || log "No $CONF_FILE found -- using defaults (copy deploy.conf.example to override)."
# shellcheck source=/dev/null
[[ -f "$CONF_FILE" ]] && source "$CONF_FILE"

: "${BASE_DIR:=/opt/aims}"
: "${BACKUP_DIR:=$BASE_DIR/backups}"
: "${KEEP_BACKUPS:=7}"
: "${SERVICE_NAME:=aims-webfrontend}"
: "${APPSETTINGS_FILE:=$BASE_DIR/shared/appsettings.Production.json}"

SUBCOMMAND="${1:-}"
[[ -n "$SUBCOMMAND" ]] || die "Usage: $0 {backup [conn]|restore <file> [--yes]|list}"

# --- sqlplus discovery (Instant Client is fine; export its lib dir) ---
SQLPLUS="$(command -v sqlplus 2>/dev/null || true)"
if [[ -z "$SQLPLUS" ]]; then
    for d in /opt/oracle/instantclient_*; do
        [[ -x "$d/sqlplus" ]] && SQLPLUS="$d/sqlplus" && break
    done
fi
[[ -n "$SQLPLUS" ]] || die "sqlplus not found (install the Oracle Instant Client or add it to PATH)."
export LD_LIBRARY_PATH="$(dirname "$SQLPLUS")${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
log "Using $SQLPLUS"

# --- connection string: positional arg > env var > appsettings ---
CONN=""
resolve_conn() {
    if [[ -n "${1:-}" ]]; then CONN="$1"; return; fi
    if [[ -n "${DB_CONNECTION_STRING:-}" ]]; then CONN="$DB_CONNECTION_STRING"; return; fi
    local ap="$APPSETTINGS_FILE"
    [[ -f "$ap" ]] || die "Not found: $ap (pass a connection string, set DB_CONNECTION_STRING, or run this after deploy.sh)."
    command -v python3 >/dev/null || die "python3 is required to parse $ap."
    mapfile -t _info < <(python3 - "$ap" <<'PY'
import json, re, sys
with open(sys.argv[1]) as f:
    data = json.load(f)
conn = data["ConnectionStrings"]["Oracle"]
parts = dict(kv.split("=", 1) for kv in conn.strip().rstrip(";").split(";") if "=" in kv)
m = re.match(r"\s*([^:/]+):(\d+)/(.+)\s*$", parts["Data Source"])
if not m:
    sys.exit("Could not parse Data Source: " + parts["Data Source"])
host, port, service = m.groups()
print("%s/%s@//%s:%s/%s" % (parts["User Id"], parts["Password"], host, port, service))
PY
)
    [[ "${#_info[@]}" -eq 1 ]] || die "Could not parse Oracle connection string out of $ap."
    CONN="${_info[0]}"
}

# The generator SQL below is built with Oracle q'[...]' literals so the
# embedded single quotes never need escaping; the heredocs are unquoted so
# bash vars ($BF, $GF, $DB_USER, $STAMP) interpolate.
case "$SUBCOMMAND" in
  backup)
    resolve_conn "${2:-}"
    DB_USER="${CONN%%/*}"
    log "Target schema: $DB_USER"
    mkdir -p "$BACKUP_DIR"
    STAMP="$(date -u +%Y%m%d%H%M%S)"
    BF="aims_${STAMP}.sql"; GF="gen_${STAMP}.sql"; LOGF="aims_${STAMP}.log"
    set +e
    ( cd "$BACKUP_DIR" && "$SQLPLUS" -S -L "$CONN" >"$LOGF" 2>&1 <<SQL
SET ECHO OFF FEEDBACK OFF HEADING OFF VERIFY OFF DEFINE OFF
SET PAGESIZE 0 LINESIZE 32767 TRIMSPOOL ON LONG 32000 LONGCHUNKSIZE 32000
SET NULL ""
SET TERMOUT OFF
WHENEVER SQLERROR EXIT SQL.SQLCODE
ALTER SESSION SET NLS_NUMERIC_CHARACTERS='.,';
SPOOL $BF
PROMPT SET ECHO OFF FEEDBACK OFF HEADING OFF VERIFY OFF DEFINE OFF
PROMPT SET PAGESIZE 0 LINESIZE 32767 TRIMSPOOL ON LONG 32000 LONGCHUNKSIZE 32000
PROMPT SET NULL ""
PROMPT ALTER SESSION SET NLS_NUMERIC_CHARACTERS='.,';
PROMPT -- ===== AIMS schema + data backup, user $DB_USER, generated $STAMP (UTC) =====
PROMPT -- Restore with: ./deploy/db_backup_sqlplus.sh restore $BF [--yes]
SELECT DBMS_METADATA.GET_DDL('TABLE', table_name) || ';'
FROM user_tables t
ORDER BY (SELECT COUNT(*) FROM user_constraints r JOIN user_constraints p ON r.r_constraint_name = p.constraint_name AND r.constraint_type = 'R' WHERE p.table_name = t.table_name) DESC, t.table_name;
SELECT DBMS_METADATA.GET_DDL('INDEX', index_name) || ';'
FROM user_indexes
WHERE table_name IN (SELECT table_name FROM user_tables)
  AND generated = 'N' AND index_type NOT IN ('IOT - TOP', 'IOT - SECONDARY')
ORDER BY index_name;
SELECT DBMS_METADATA.GET_DDL('SEQUENCE', sequence_name) || ';' FROM user_sequences ORDER BY sequence_name;
SELECT DBMS_METADATA.GET_DDL('TRIGGER', trigger_name) || ';' FROM user_triggers ORDER BY trigger_name;
PROMPT -- DATA
SPOOL OFF
-- guard: abort rather than silently truncate an oversized CLOB
DECLARE
  v_cnt NUMBER;
BEGIN
  FOR c IN (SELECT table_name, column_name FROM user_tab_columns
            WHERE data_type IN ('CLOB', 'NCLOB')) LOOP
    EXECUTE IMMEDIATE 'SELECT COUNT(*) FROM "' || c.table_name || '" WHERE DBMS_LOB.GETLENGTH("' || c.column_name || '") > 15000' INTO v_cnt;
    IF v_cnt > 0 THEN
      RAISE_APPLICATION_ERROR(-20001, 'Column ' || c.table_name || '.' || c.column_name || ' has values > 15000 chars -- CLOB backup limit.');
    END IF;
  END LOOP;
END;
/
-- emit one SELECT per table; running it later produces the INSERT lines
SPOOL $GF
SELECT 'SELECT ' || q'['INSERT INTO "]' || table_name || q'[" VALUES (' || ]' ||
       LISTAGG(expr, q'[ || ',' || ]') WITHIN GROUP (ORDER BY column_id) ||
       q'[ || ');' FROM "]' || table_name || q'[";]'
FROM (
  SELECT t.table_name, c.column_id,
    (SELECT COUNT(*) FROM user_constraints r JOIN user_constraints p ON r.r_constraint_name = p.constraint_name AND r.constraint_type = 'R' WHERE p.table_name = t.table_name) AS refs,
    CASE c.data_type
      WHEN 'NUMBER' THEN q'[NVL(TO_CHAR("]' || c.column_name || q'["),'NULL')]'
      WHEN 'DATE' THEN q'[NVL('TO_DATE('''||TO_CHAR("]' || c.column_name || q'[",'YYYY-MM-DD HH24:MI:SS')||''',''YYYY-MM-DD HH24:MI:SS'')','NULL')]'
      WHEN 'RAW' THEN q'[NVL(''''||LOWER(RAWTOHEX("]' || c.column_name || q'["))||'''','NULL')]'
      ELSE CASE
        WHEN c.data_type LIKE 'TIMESTAMP%' AND c.data_type LIKE '%TIME ZONE' THEN
          q'[NVL('TO_TIMESTAMP_TZ('''||TO_CHAR("]' || c.column_name || q'[",'YYYY-MM-DD HH24:MI:SS.FF TZH:TZM')||''',''YYYY-MM-DD HH24:MI:SS.FF TZH:TZM'')','NULL')]'
        WHEN c.data_type LIKE 'TIMESTAMP%' THEN
          q'[NVL('TO_TIMESTAMP('''||TO_CHAR("]' || c.column_name || q'[",'YYYY-MM-DD HH24:MI:SS.FF')||''',''YYYY-MM-DD HH24:MI:SS.FF'')','NULL')]'
        ELSE q'[NVL(''''||REPLACE("]' || c.column_name || q'[",'''','''''')||'''','NULL')]'
      END
    END AS expr
  FROM user_tab_columns c
  JOIN user_tables t ON t.table_name = c.table_name
  WHERE c.data_type NOT IN ('LONG', 'LONG RAW', 'BLOB', 'BFILE', 'CLOB', 'NCLOB')
  UNION ALL
  SELECT t.table_name, c.column_id,
    (SELECT COUNT(*) FROM user_constraints r JOIN user_constraints p ON r.r_constraint_name = p.constraint_name AND r.constraint_type = 'R' WHERE p.table_name = t.table_name) AS refs,
    q'[NVL(''''||REPLACE("]' || c.column_name || q'[",'''','''''')||'''','NULL')]' AS expr
  FROM user_tab_columns c
  JOIN user_tables t ON t.table_name = c.table_name
  WHERE c.data_type IN ('CLOB', 'NCLOB')
)
GROUP BY table_name, refs
ORDER BY refs DESC, table_name;
SPOOL OFF
-- run the generated row queries, appending the INSERTs to the backup
SPOOL $BF APPEND
PROMPT SET ECHO OFF FEEDBACK OFF HEADING OFF VERIFY OFF DEFINE OFF
PROMPT SET PAGESIZE 0 LINESIZE 32767 TRIMSPOOL ON
PROMPT SET NULL ""
@$GF
SPOOL OFF
EXIT
SQL
)
    RC=$?
    set -e
    rm -f "$BACKUP_DIR/$GF"
    if [[ $RC -ne 0 ]] || grep -qiE 'ORA-|SP2-|PLS-|overflow' "$BACKUP_DIR/$LOGF"; then
        die "Backup failed (exit $RC) -- see $BACKUP_DIR/$LOGF ($(grep -im1 -E 'ORA-|SP2-|PLS-|overflow' "$BACKUP_DIR/$LOGF" | tr -d '\r'))"
    fi
    grep -q 'CREATE TABLE' "$BACKUP_DIR/$BF" || die "Backup contains no tables -- connected to the wrong schema? See $BACKUP_DIR/$LOGF"
    log "Backup written: $BACKUP_DIR/$BF ($(du -h "$BACKUP_DIR/$BF" | cut -f1), $(grep -c '^INSERT INTO' "$BACKUP_DIR/$BF") insert rows)"
    log "Pruning old backups (keeping $KEEP_BACKUPS) ..."
    ls -1t "$BACKUP_DIR"/aims_*.sql 2>/dev/null | tail -n +"$((KEEP_BACKUPS + 1))" | while read -r old; do
        rm -f "$old" "${old%.sql}.log"
    done
    ;;

  restore)
    BF="${2:-}"
    [[ -n "$BF" ]] || die "Usage: $0 restore <backup.sql> [--yes]"
    ASSUME_YES=0
    [[ "${3:-}" == "--yes" || "${3:-}" == "-y" ]] && ASSUME_YES=1
    if [[ -f "$BF" ]]; then
        BF_PATH="$BF"
    elif [[ -f "$BACKUP_DIR/$BF" ]]; then
        BF_PATH="$BACKUP_DIR/$BF"
    else
        die "Backup file not found: $BF (looked in . and $BACKUP_DIR)"
    fi
    BF_NAME="$(basename "$BF_PATH")"
    resolve_conn
    DB_USER="${CONN%%/*}"
    if [[ "$ASSUME_YES" -ne 1 ]]; then
        echo "This will DROP all tables of schema '$DB_USER' and replace them"
        echo "with the contents of $BF_PATH. This cannot be undone."
        read -r -p "Type 'yes' to continue: " CONFIRM
        [[ "$CONFIRM" == "yes" ]] || die "Aborted."
    fi
    mkdir -p "$BACKUP_DIR"
    LOGF="restore_${BF_NAME%.sql}.log"
    set +e
    ( cd "$BACKUP_DIR" && "$SQLPLUS" -S -L "$CONN" >"$LOGF" 2>&1 <<SQL
SET ECHO OFF FEEDBACK OFF HEADING OFF VERIFY OFF DEFINE OFF
SET PAGESIZE 0 LINESIZE 32767 TRIMSPOOL ON
SET TERMOUT OFF
WHENEVER SQLERROR EXIT SQL.SQLCODE
SPOOL drop_$BF_NAME.sql
SELECT 'DROP TABLE "' || table_name || '" CASCADE CONSTRAINTS;' FROM user_tables;
SPOOL OFF
@drop_$BF_NAME.sql
SET NULL ""
@$BF_NAME
-- re-bump sequences above MAX(Id) so nextval cannot collide with restored rows
DECLARE
  v_tab VARCHAR2(128);
  v_max NUMBER;
BEGIN
  FOR s IN (SELECT sequence_name FROM user_sequences ORDER BY sequence_name) LOOP
    v_tab := NULL;
    SELECT MAX(table_name) INTO v_tab FROM user_tables
     WHERE table_name = SUBSTR(s.sequence_name, 1, LENGTH(s.sequence_name) - 4);
    IF v_tab IS NULL THEN
      FOR tr IN (SELECT table_name FROM user_triggers
                 WHERE INSTR(DBMS_METADATA.GET_DDL('TRIGGER', trigger_name), UPPER(s.sequence_name)) > 0) LOOP
        v_tab := tr.table_name;
        EXIT;
      END LOOP;
    END IF;
    IF v_tab IS NOT NULL THEN
      EXECUTE IMMEDIATE 'SELECT NVL(MAX("Id"), 0) + 1 FROM "' || v_tab || '"' INTO v_max;
      EXECUTE IMMEDIATE 'DROP SEQUENCE "' || s.sequence_name || '"';
      EXECUTE IMMEDIATE 'CREATE SEQUENCE "' || s.sequence_name || '" START WITH ' || v_max || ' INCREMENT BY 1 NOCACHE NOCYCLE';
    END IF;
  END LOOP;
END;
/
EXIT
SQL
)
    RC=$?
    set -e
    rm -f "$BACKUP_DIR/drop_$BF_NAME.sql"
    if [[ $RC -ne 0 ]] || grep -qiE 'ORA-|SP2-|PLS-|overflow' "$BACKUP_DIR/$LOGF"; then
        die "Restore failed (exit $RC) -- see $BACKUP_DIR/$LOGF ($(grep -im1 -E 'ORA-|SP2-|PLS-|overflow' "$BACKUP_DIR/$LOGF" | tr -d '\r'))"
    fi
    log "Restore complete. See $BACKUP_DIR/$LOGF for the sqlplus transcript."
    log "Restart the app so pooled connections pick up the restored data: sudo systemctl restart $SERVICE_NAME"
    ;;

  list)
    ls -lht "$BACKUP_DIR"/aims_*.sql 2>/dev/null || echo "No backups found in $BACKUP_DIR"
    ;;

  *)
    die "Unknown subcommand '$SUBCOMMAND'. Usage: $0 {backup [conn]|restore <file> [--yes]|list}"
    ;;
esac
