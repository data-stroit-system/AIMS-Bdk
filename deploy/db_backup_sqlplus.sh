#!/usr/bin/env bash
#
# Connection-string-only Oracle backup/restore for AIMS.
#
# This script needs NOTHING but a working sqlplus and a plain user/password
# connection string: no expdp/impdp, no local Oracle home, no OS
# authentication, no DIRECTORY objects. Works against a remote Oracle too,
# as long as the connected user owns the schema being dumped (it exports
# that user's own tables).
#
#   backup  = schema DDL (dbms_metadata: tables incl. constraints, indexes,
#             sequences, triggers) + full data as INSERT statements, in one
#             .sql file.
#   restore = drops ALL tables and sequences of the connected user's schema (a clean wipe),
#             replays the backup file, then re-bumps every sequence above
#             MAX(Id) so the app's NEXTVAL can never collide with restored
#             rows (the app assigns Id via BEFORE-INSERT triggers that read
#             <Table>_SEQ; explicit-Id INSERTs bypass them).
#
# Schema-agnostic: the DDL is emitted without TABLESPACE/STORAGE clauses and
# without the source user's name, so a backup taken as one user (prod "aims")
# can be restored into a DIFFERENT user/schema (dev "system"); tables are
# created in the target user's default tablespace (needs quota there).
# restore also re-strips source-schema qualifiers and TABLESPACE clauses
# from backups made by older versions of this script.
#
# Requirements / limits (all fine for AIMS as of 2026-08):
#   - Oracle 11gR2+ (uses LISTAGG, q-quoting, DBMS_METADATA, DBMS_LOB).
#     sqlplus from the Instant Client is fine.
#   - FK chains of any depth are ordered (parents before children, via a
#     CONNECT BY depth query); restore also reorders the table DDL and
#     INSERT blocks of backups made by older versions (python3).
#   - CLOB/NCLOB values must fit 15,000 chars; the backup fails
#     loudly instead of truncating. No LONG/BLOB/BFILE columns.
#
# Usage (from the repo root):
#   ./deploy/db_backup_sqlplus.sh backup [connection-string]
#   ./deploy/db_backup_sqlplus.sh restore <backup.sql> [--yes]
#   ./deploy/db_backup_sqlplus.sh list
#
# connection-string: 'user/password@//host:port/service'
# Also honoured: DB_CONNECTION_STRING env var, or fall back to reading the
# Oracle string out of $APPSETTINGS_FILE (default
# $BASE_DIR/shared/appsettings.Production.json, override in deploy.conf,
# parsed with python3).
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
-- Emit DDL that can be replayed in ANY schema: no TABLESPACE/STORAGE
-- clauses (the target user may have no quota on the source's tablespace)
-- and no schema name (triggers would otherwise come out as
-- "SRCUSER"."TRG", which a different user cannot create).
BEGIN
  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'EMIT_SCHEMA', FALSE);
  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'SEGMENT_ATTRIBUTES', FALSE);
  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'STORAGE', FALSE);
  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'TABLESPACE', FALSE);
END;
/
SPOOL $BF
PROMPT SET ECHO OFF FEEDBACK OFF HEADING OFF VERIFY OFF DEFINE OFF
PROMPT SET PAGESIZE 0 LINESIZE 32767 TRIMSPOOL ON LONG 32000 LONGCHUNKSIZE 32000
PROMPT SET NULL ""
PROMPT ALTER SESSION SET NLS_NUMERIC_CHARACTERS='.,';;
PROMPT -- ===== AIMS schema + data backup, user $DB_USER, generated $STAMP (UTC) =====
PROMPT -- Restore with: ./deploy/db_backup_sqlplus.sh restore $BF [--yes]
-- Tables are ordered by FK depth (parents first) via CONNECT BY. The old
-- inbound-FK-count ordering only worked for single-level chains and put
-- ASSETITEMS (referenced by 2 tables) before PLANTS (referenced by 1) even
-- though ASSETITEMS itself references PLANTS -- replay then died with
-- ORA-00942 on the REFERENCES clause.
WITH fk AS (
  SELECT r.table_name child, p.table_name parent
  FROM user_constraints r
  JOIN user_constraints p
    ON r.r_constraint_name = p.constraint_name
   AND p.constraint_type IN ('P','U')
  WHERE r.constraint_type = 'R'
),
depth AS (
  SELECT child, MAX(LEVEL) AS depth
  FROM fk
  START WITH child IS NOT NULL
  CONNECT BY NOCYCLE PRIOR child = parent
  GROUP BY child
)
SELECT REPLACE(DBMS_METADATA.GET_DDL('TABLE', t.table_name), '"' || USER || '".', '') || ';'
FROM user_tables t
LEFT JOIN depth d ON d.child = t.table_name
ORDER BY NVL(d.depth, 0), t.table_name;
-- constraint-backed indexes only (PK/unique constraints recreate their own
-- index inline on replay -- an explicit CREATE INDEX for the same name would
-- ORA-00955; XE 11g reports generated='N' for them, so exclude by constraint
-- name instead)
SELECT REPLACE(DBMS_METADATA.GET_DDL('INDEX', index_name), '"' || USER || '".', '') || ';'
FROM user_indexes
WHERE table_name IN (SELECT table_name FROM user_tables)
  AND generated = 'N' AND index_type NOT IN ('IOT - TOP', 'IOT - SECONDARY')
  AND index_name NOT IN (SELECT constraint_name FROM user_constraints)
ORDER BY index_name;
SELECT REPLACE(DBMS_METADATA.GET_DDL('SEQUENCE', sequence_name), '"' || USER || '".', '') || ';' FROM user_sequences ORDER BY sequence_name;
-- Trigger DDL embeds a PL/SQL body, and SQL*Plus (23+) does NOT execute a
-- PL/SQL block on "END;" alone when reading a replayed file -- the buffer
-- silently swallows everything after it (the ALTER line, all INSERTs). Split
-- END; from the ALTER with an explicit "/" so the buffer actually runs.
SELECT REPLACE(
         REPLACE(DBMS_METADATA.GET_DDL('TRIGGER', trigger_name), '"' || USER || '".', ''),
         'END;' || CHR(10) || 'ALTER TRIGGER',
         'END;' || CHR(10) || '/' || CHR(10) || 'ALTER TRIGGER') || ';'
FROM user_triggers ORDER BY trigger_name;
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
WITH fk AS (
  SELECT r.table_name child, p.table_name parent
  FROM user_constraints r
  JOIN user_constraints p
    ON r.r_constraint_name = p.constraint_name
   AND p.constraint_type IN ('P','U')
  WHERE r.constraint_type = 'R'
),
depth AS (
  SELECT child, MAX(LEVEL) AS depth
  FROM fk
  START WITH child IS NOT NULL
  CONNECT BY NOCYCLE PRIOR child = parent
  GROUP BY child
)
SELECT 'SELECT ' || q'['INSERT INTO "]' || x.table_name || q'[" VALUES (' || ]' ||
       LISTAGG(x.expr, q'[ || ',' || ]') WITHIN GROUP (ORDER BY x.column_id) ||
       q'[ || ');' FROM "]' || x.table_name || q'[";]'
FROM (
  SELECT t.table_name, c.column_id,
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
    q'[NVL(''''||REPLACE("]' || c.column_name || q'[",'''','''''')||'''','NULL')]' AS expr
  FROM user_tab_columns c
  JOIN user_tables t ON t.table_name = c.table_name
  WHERE c.data_type IN ('CLOB', 'NCLOB')
) x
LEFT JOIN depth d ON d.child = x.table_name
GROUP BY x.table_name, NVL(d.depth, 0)
ORDER BY NVL(d.depth, 0), x.table_name;
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
        echo "This will DELETE ALL tables and sequences of schema '$DB_USER'"
        echo "and replace them with the contents of $BF_PATH. This cannot be"
        echo "undone."
        read -r -p "Type 'yes' to continue: " CONFIRM
        [[ "$CONFIRM" == "yes" ]] || die "Aborted."
    fi
    mkdir -p "$BACKUP_DIR"
    # Normalize the file for the target schema before replay. Backups made by
    # older versions carry the source user's name on trigger DDL
    # ("SRCUSER"."TRG" -- creating a trigger as another user is an ORA-01031)
    # and TABLESPACE clauses (a different target user, e.g. dev's "system",
    # usually has no quota on TABLESPACE "USERS" -- ORA-01950). Strip both,
    # but only in the DDL region above the "-- DATA" marker, so INSERT
    # payloads are never rewritten.
    SRC_SCHEMA="$(grep -m1 -oE 'TRIGGER "[^"]+"\.' "$BF_PATH" | sed -E 's/.*TRIGGER "([^"]+)"\./\1/' || true)"
    SAN="$BACKUP_DIR/sanitized_$BF_NAME"
    if grep -q '^-- DATA$' "$BF_PATH"; then
        # also repair the unterminated "ALTER SESSION SET NLS_NUMERIC_CHARACTERS"
        # line older backups carry (a PROMPT swallowed its ';', which makes the
        # replayed buffer merge with the following CREATE TABLE -- ORA-00933)
        SED_ARGS=(-e "/^-- DATA/,\$ b" \
                  -e 's/[[:space:]]*TABLESPACE[[:space:]]*"[^"]*"//g' \
                  -e "s|^[[:space:]]*ALTER SESSION SET NLS_NUMERIC_CHARACTERS='\.,'\$|&;|" \
                  -e '/^END;$/{N;/^END;\n\/$/b;s/^END;\n/END;\n\/\n/}')
        [[ -n "$SRC_SCHEMA" ]] && SED_ARGS+=(-e "s/\"${SRC_SCHEMA}\"\.//g")
        sed "${SED_ARGS[@]}" "$BF_PATH" > "$SAN"
    else
        cp "$BF_PATH" "$SAN"
    fi
    if [[ -n "$SRC_SCHEMA" ]]; then
        log "Backup was taken as '$SRC_SCHEMA' -- stripping schema qualifiers and TABLESPACE clauses (restoring into '$DB_USER')."
    fi
    # Reorder tables and their INSERT blocks topologically (FK parents first).
    # Backups from older versions ordered tables by inbound-FK count, which
    # only works for single-level chains -- ASSETITEMS references PLANTS but
    # was emitted first, and the replay died with ORA-00942.
    if command -v python3 >/dev/null; then
        if REORDER_OUT="$(python3 - "$SAN" <<'PY'
import re, sys

path = sys.argv[1]
with open(path, encoding='utf-8', errors='surrogateescape') as f:
    lines = f.readlines()

data_idx = None
for i, l in enumerate(lines):
    if l.strip() == '-- DATA':
        data_idx = i
        break
if data_idx is None:
    sys.exit(0)  # no marker: nothing to reorder safely

ddl, data = lines[:data_idx], lines[data_idx:]

# split the DDL section into blocks: CREATE TABLE blocks vs everything else
blocks = []                      # (kind, name, [lines])
cur = None
for l in ddl:
    m = re.match(r'^\s*CREATE TABLE "([^"]+)"', l)
    if m:
        if cur is not None:
            blocks.append(cur)
        cur = ('table', m.group(1), [l])
        continue
    if re.match(r'^\s*(CREATE (?:UNIQUE )?INDEX|CREATE SEQUENCE|CREATE OR REPLACE TRIGGER)', l):
        if cur is not None:
            blocks.append(cur)
        cur = ('other', None, [l])
        continue
    if cur is not None:
        cur[2].append(l)
    else:
        blocks.append(('other', None, [l]))
if cur is not None:
    blocks.append(cur)

names = [n for k, n, _ in blocks if k == 'table']

# FK edges: table -> parents it references
refs = {n: [] for n in names}
for k, n, ls in blocks:
    if k != 'table':
        continue
    for p in re.findall(r'REFERENCES\s+"([^"]+)"', ''.join(ls)):
        if p in names and p != n and p not in refs[n]:
            refs[n].append(p)

def depth(n, seen):
    if n in seen:
        return 0                     # FK cycle guard
    seen = seen | {n}
    return 1 + max((depth(p, seen) for p in refs[n]), default=0)

order = sorted(names, key=lambda n: (depth(n, frozenset()), names.index(n)))
orig = [n for k, n, _ in blocks if k == 'table']
if order == orig:
    sys.exit(0)                      # already correct

# rebuild the DDL section: preamble, reordered table blocks, then the
# sections that followed them (indexes/sequences/triggers stay put)
pre, tables, post = [], {}, []
section = 'pre'
for k, n, ls in blocks:
    if k == 'table':
        tables[n] = ls
        section = 'tables'
    elif section == 'pre':
        pre.extend(ls)
    else:
        post.extend(ls)
new_ddl = pre[:]
for n in order:
    new_ddl += tables[n]
new_ddl += post

# data section: keep non-INSERT segments in place, reorder INSERT blocks
segs = []                          # (kind, table, [lines])
cur = None
for l in data:
    m = re.match(r'^INSERT INTO "([^"]+)"', l)
    if m:
        if cur is not None:
            segs.append(cur)
        cur = ('ins', m.group(1), [l])
    else:
        if cur is not None and cur[0] == 'ins':
            segs.append(cur)
            cur = None
        if cur is None:
            cur = ('other', None, [l])
        else:
            cur[2].append(l)
if cur is not None:
    segs.append(cur)

by_table = {}
for kind, t, ls in segs:
    if kind == 'ins':
        by_table.setdefault(t, []).append(ls)

new_data = []
for kind, t, ls in segs:
    if kind == 'other':
        new_data.extend(ls)            # header lines stay up top
for t in order:
    for blk in by_table.get(t, []):    # INSERT blocks in FK order
        new_data.extend(blk)
for t in by_table:
    if t not in order:
        for blk in by_table[t]:        # tables missing from the DDL order
            new_data.extend(blk)

with open(path, 'w', encoding='utf-8', errors='surrogateescape', newline='') as f:
    f.writelines(new_ddl)
    f.writelines(new_data)
print(', '.join(order))
PY
)"; then
            [[ -n "$REORDER_OUT" ]] && log "Reordered tables in backup (FK parents first): $REORDER_OUT"
        else
            die "Failed to reorder backup DDL ($SAN)."
        fi
    else
        log "python3 not found -- skipping FK-order repair of the backup DDL."
    fi
    LOGF="restore_${BF_NAME%.sql}.log"
    set +e
    ( cd "$BACKUP_DIR" && "$SQLPLUS" -S -L "$CONN" >"$LOGF" 2>&1 <<SQL
SET ECHO OFF FEEDBACK OFF HEADING OFF VERIFY OFF DEFINE OFF
SET PAGESIZE 0 LINESIZE 32767 TRIMSPOOL ON
SET TERMOUT OFF
WHENEVER SQLERROR EXIT SQL.SQLCODE
SPOOL drop_$BF_NAME.sql
SELECT 'DROP TABLE "' || table_name || '" CASCADE CONSTRAINTS;' FROM user_tables;
SELECT 'DROP SEQUENCE "' || sequence_name || '";' FROM user_sequences;
SPOOL OFF
-- Dropping is best-effort: AQ queue tables (e.g. the DEF-AQ leftovers on
-- SYSTEM) reject plain DROP TABLE with ORA-24005, and Oracle-internal views
-- like USER_QUEUE_TABLES may be invalid after a wipe -- leftover tables of
-- that kind can't collide with the backup's CREATE TABLEs, so let them be.
-- A leftover AIMS table (e.g. locked) still fails the replay with ORA-00955.
WHENEVER SQLERROR CONTINUE
@drop_$BF_NAME.sql
WHENEVER SQLERROR EXIT SQL.SQLCODE
SET NULL ""
@sanitized_$BF_NAME
-- re-bump sequences above MAX(Id) so nextval cannot collide with restored rows
-- (the Id column is "ID" in the app's unquoted-DDL tables but "Id" in older
-- hand-created schemas -- resolve it case-insensitively)
DECLARE
  v_tab VARCHAR2(128);
  v_id_col VARCHAR2(128);
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
      v_id_col := NULL;
      BEGIN
        SELECT MAX(column_name) INTO v_id_col FROM user_tab_columns
         WHERE table_name = v_tab AND UPPER(column_name) = 'ID';
      EXCEPTION WHEN NO_DATA_FOUND THEN v_id_col := NULL;
      END;
      IF v_id_col IS NOT NULL THEN
        EXECUTE IMMEDIATE 'SELECT NVL(MAX("' || v_id_col || '"), 0) + 1 FROM "' || v_tab || '"' INTO v_max;
        EXECUTE IMMEDIATE 'DROP SEQUENCE "' || s.sequence_name || '"';
        EXECUTE IMMEDIATE 'CREATE SEQUENCE "' || s.sequence_name || '" START WITH ' || v_max || ' INCREMENT BY 1 NOCACHE NOCYCLE';
      END IF;
    END IF;
  END LOOP;
END;
/
EXIT
SQL
)
    RC=$?
    set -e
    rm -f "$BACKUP_DIR/drop_$BF_NAME.sql" "$BACKUP_DIR/sanitized_$BF_NAME"
    if [[ $RC -ne 0 ]] || grep -qiE 'ORA-|SP2-|PLS-|overflow' "$BACKUP_DIR/$LOGF"; then
        die "Restore failed (exit $RC) -- see $BACKUP_DIR/$LOGF ($(grep -im1 -E 'ORA-|SP2-|PLS-|overflow' "$BACKUP_DIR/$LOGF" | tr -d '\r'))"
    fi
    log "Restore complete (schema '$DB_USER'). See $BACKUP_DIR/$LOGF for the sqlplus transcript."
    log "Restart the app so pooled connections pick up the restored data: sudo systemctl restart $SERVICE_NAME"
    ;;

  list)
    ls -lht "$BACKUP_DIR"/aims_*.sql 2>/dev/null || echo "No backups found in $BACKUP_DIR"
    ;;

  *)
    die "Unknown subcommand '$SUBCOMMAND'. Usage: $0 {backup [conn]|restore <file> [--yes]|list}"
    ;;
esac
