#!/usr/bin/env bash
#
# Backup and restore the AIMS Oracle schema via Data Pump (expdp/impdp).
#
# Run ON THE SERVER (Oracle lives on the same host as the app in this
# deployment model -- see deploy.sh), from a checkout of this repo. Reads
# the Oracle connection string straight out of the deployed
# $BASE_DIR/shared/appsettings.Production.json, same as deploy.sh writes
# it, so there's nothing extra to configure beyond deploy.conf (the same
# gitignored file deploy.sh/upload.sh already use).
#
# Data Pump needs an Oracle DIRECTORY object -- a DB-side alias for a
# filesystem path the Oracle server process can read/write -- and creating
# one needs a DBA connection. We get that via OS authentication
# ("/ as sysdba") instead of requiring a separate SYS/SYSTEM password
# nobody has documented for prod, exactly like setup_oracle_xe.sh does:
#   - if Oracle is the podman container setup_oracle_xe.sh created
#     (auto-detected by container name), everything runs via
#     `podman exec --user oracle`, and the dump file is `podman cp`'d
#     in/out of the container.
#   - otherwise this assumes a native install and runs as the host's
#     `oracle` OS user via `sudo -u oracle -i` (a login shell, so its own
#     oraenv/profile sets ORACLE_HOME/ORACLE_SID -- no path guessing).
#
# The DB password is never placed on a command line or process argument
# (both podman exec and sudo -u leave those visible to `ps`) -- it only
# ever goes into a Data Pump parameter file written with tightened
# permissions and deleted right after use.
#
# Usage (from the repo root):
#   ./deploy/db_backup.sh backup
#   ./deploy/db_backup.sh restore <dumpfile.dmp|path> [--yes]
#   ./deploy/db_backup.sh list
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
: "${ORACLE_CONTAINER_NAME:=oracle-xe-11g}"              # must match setup_oracle_xe.sh
: "${ORACLE_HOME:=/u01/app/oracle/product/11.2.0/xe}"    # container mode only
: "${ORACLE_SID:=XE}"                                    # container mode only

DIR_OBJECT="AIMS_DPDUMP_DIR"
CONTAINER_DUMP_PATH="/tmp/aims_dpdump"

command -v python3 >/dev/null || die "python3 is required (used to parse appsettings.Production.json)."

SUBCOMMAND="${1:-}"
[[ -n "$SUBCOMMAND" ]] || die "Usage: $0 {backup|restore <file>|list} [--yes]"
shift

# ---------------------------------------------------------------------------
# Read the Oracle connection string the app itself is configured with, so
# backup/restore always target exactly the DB the running app points at.
# ---------------------------------------------------------------------------
APPSETTINGS="$BASE_DIR/shared/appsettings.Production.json"
[[ -f "$APPSETTINGS" ]] || die "Not found: $APPSETTINGS (run this on the server after deploy.sh has run at least once)."

mapfile -t _dbinfo < <(python3 - "$APPSETTINGS" <<'PY'
import json, re, sys
with open(sys.argv[1]) as f:
    data = json.load(f)
conn = data["ConnectionStrings"]["Oracle"]
parts = dict(kv.split("=", 1) for kv in conn.strip().rstrip(";").split(";") if "=" in kv)
m = re.match(r"\s*([^:/]+):(\d+)/(.+)\s*$", parts["Data Source"])
if not m:
    sys.exit("Could not parse Data Source: " + parts["Data Source"])
host, port, service = m.groups()
for v in (host, port, service, parts["User Id"], parts["Password"]):
    print(v)
PY
)
[[ "${#_dbinfo[@]}" -eq 5 ]] || die "Could not parse Oracle connection string out of $APPSETTINGS."
DB_HOST="${_dbinfo[0]}"; DB_PORT="${_dbinfo[1]}"; DB_SERVICE="${_dbinfo[2]}"
DB_USER="${_dbinfo[3]}"; DB_PASSWORD="${_dbinfo[4]}"
CONNECT_STR="//$DB_HOST:$DB_PORT/$DB_SERVICE"

# ---------------------------------------------------------------------------
# Container vs native Oracle
# ---------------------------------------------------------------------------
USE_CONTAINER=0
if command -v podman >/dev/null && podman ps --format '{{.Names}}' 2>/dev/null | grep -qx "$ORACLE_CONTAINER_NAME"; then
    USE_CONTAINER=1
    log "Oracle detected as podman container '$ORACLE_CONTAINER_NAME'."
else
    log "No '$ORACLE_CONTAINER_NAME' container running -- assuming a native Oracle install."
    sudo -v
fi

sql_as_sysdba() {
    # $1 = SQL to run via OS-authenticated `sqlplus / as sysdba` (no
    # password involved -- OS authentication only).
    if [[ "$USE_CONTAINER" -eq 1 ]]; then
        podman exec -i --user oracle "$ORACLE_CONTAINER_NAME" bash -c \
            "ORACLE_HOME=$ORACLE_HOME ORACLE_SID=$ORACLE_SID LD_LIBRARY_PATH=$ORACLE_HOME/lib $ORACLE_HOME/bin/sqlplus -S / as sysdba" <<<"$1"
    else
        sudo -u oracle -i sqlplus -S / as sysdba <<<"$1"
    fi
}

# Runs expdp/impdp with options supplied as a Data Pump parameter file, so
# the connect string (including password) never appears in a command line.
# $1 = expdp|impdp, $2 = parfile body (without the userid= line)
run_datapump() {
    local tool="$1" extra_par="$2"
    local par_body="userid=$DB_USER/$DB_PASSWORD@$CONNECT_STR
$extra_par"
    local local_tmp
    local_tmp="$(mktemp)"
    printf '%s\n' "$par_body" >"$local_tmp"

    if [[ "$USE_CONTAINER" -eq 1 ]]; then
        # podman cp preserves the source file's mode; mktemp defaults to
        # 600 which the container's non-root oracle user can't read once
        # cp changes the owner to root -- widen it first (matches
        # setup_oracle_xe.sh's UNLOCK_SQL/CREATE_SQL handling).
        chmod 644 "$local_tmp"
        podman cp "$local_tmp" "$ORACLE_CONTAINER_NAME:$CONTAINER_DUMP_PATH/dp.par"
        rm -f "$local_tmp"
        podman exec -i --user oracle "$ORACLE_CONTAINER_NAME" bash -c \
            "ORACLE_HOME=$ORACLE_HOME ORACLE_SID=$ORACLE_SID LD_LIBRARY_PATH=$ORACLE_HOME/lib $ORACLE_HOME/bin/$tool parfile=$CONTAINER_DUMP_PATH/dp.par"
        podman exec --user oracle "$ORACLE_CONTAINER_NAME" rm -f "$CONTAINER_DUMP_PATH/dp.par"
    else
        sudo install -o oracle -g oracle -m 600 "$local_tmp" "$BACKUP_DIR/dp.par" 2>/dev/null \
            || sudo install -o oracle -m 600 "$local_tmp" "$BACKUP_DIR/dp.par"
        rm -f "$local_tmp"
        sudo -u oracle -i "$tool" "parfile=$BACKUP_DIR/dp.par"
        sudo rm -f "$BACKUP_DIR/dp.par"
    fi
}

ensure_directory_object() {
    # $1 = filesystem path the DIRECTORY object should point at
    local fs_path="$1"
    if [[ "$USE_CONTAINER" -eq 1 ]]; then
        podman exec --user oracle "$ORACLE_CONTAINER_NAME" mkdir -p "$fs_path"
    else
        sudo mkdir -p "$fs_path"
        # Sidesteps guessing the oracle OS user/group name across
        # different native installs: the oracle server process needs
        # write access to drop dump files here, and the deploying user
        # needs to list/prune them afterwards.
        sudo chmod 777 "$fs_path"
    fi
    sql_as_sysdba "
        CREATE OR REPLACE DIRECTORY $DIR_OBJECT AS '$fs_path';
        GRANT READ, WRITE ON DIRECTORY $DIR_OBJECT TO $DB_USER;
        EXIT;
    "
}

mkdir -p "$BACKUP_DIR"

case "$SUBCOMMAND" in
  backup)
    STAMP="$(date -u +%Y%m%d%H%M%S)"
    DUMP_NAME="aims_${STAMP}.dmp"
    LOG_NAME="aims_${STAMP}.log"
    PAR_EXTRA="directory=$DIR_OBJECT
dumpfile=$DUMP_NAME
logfile=$LOG_NAME
schemas=$DB_USER"

    if [[ "$USE_CONTAINER" -eq 1 ]]; then
        ensure_directory_object "$CONTAINER_DUMP_PATH"
        log "Exporting schema '$DB_USER' inside container $ORACLE_CONTAINER_NAME ..."
        run_datapump expdp "$PAR_EXTRA"
        log "Copying dump out of the container ..."
        podman cp "$ORACLE_CONTAINER_NAME:$CONTAINER_DUMP_PATH/$DUMP_NAME" "$BACKUP_DIR/$DUMP_NAME"
        podman cp "$ORACLE_CONTAINER_NAME:$CONTAINER_DUMP_PATH/$LOG_NAME" "$BACKUP_DIR/$LOG_NAME" 2>/dev/null || true
        podman exec --user oracle "$ORACLE_CONTAINER_NAME" rm -f "$CONTAINER_DUMP_PATH/$DUMP_NAME" "$CONTAINER_DUMP_PATH/$LOG_NAME"
    else
        ensure_directory_object "$BACKUP_DIR"
        log "Exporting schema '$DB_USER' ..."
        run_datapump expdp "$PAR_EXTRA"
    fi

    log "Backup written: $BACKUP_DIR/$DUMP_NAME"

    log "Pruning old backups (keeping $KEEP_BACKUPS) ..."
    ls -1t "$BACKUP_DIR"/aims_*.dmp 2>/dev/null | tail -n +"$((KEEP_BACKUPS + 1))" | while read -r old; do
        base="${old%.dmp}"
        rm -f "$old" "${base}.log"
    done
    ;;

  restore)
    DUMP_ARG="${1:-}"
    [[ -n "$DUMP_ARG" ]] || die "Usage: $0 restore <dumpfile.dmp|path> [--yes]"
    ASSUME_YES=0
    [[ "${2:-}" == "--yes" || "${2:-}" == "-y" ]] && ASSUME_YES=1

    if [[ -f "$DUMP_ARG" ]]; then
        DUMP_PATH="$DUMP_ARG"
    elif [[ -f "$BACKUP_DIR/$DUMP_ARG" ]]; then
        DUMP_PATH="$BACKUP_DIR/$DUMP_ARG"
    else
        die "Dump file not found: $DUMP_ARG (looked in . and $BACKUP_DIR)"
    fi
    DUMP_NAME="$(basename "$DUMP_PATH")"
    LOG_NAME="${DUMP_NAME%.dmp}_restore.log"

    if [[ "$ASSUME_YES" -ne 1 ]]; then
        echo "This will REPLACE existing tables in schema '$DB_USER' at $CONNECT_STR"
        echo "using $DUMP_PATH. This cannot be undone."
        read -r -p "Type 'yes' to continue: " CONFIRM
        [[ "$CONFIRM" == "yes" ]] || die "Aborted."
    fi

    PAR_EXTRA="directory=$DIR_OBJECT
dumpfile=$DUMP_NAME
logfile=$LOG_NAME
schemas=$DB_USER
table_exists_action=replace"

    if [[ "$USE_CONTAINER" -eq 1 ]]; then
        ensure_directory_object "$CONTAINER_DUMP_PATH"
        log "Copying dump into the container ..."
        podman cp "$DUMP_PATH" "$ORACLE_CONTAINER_NAME:$CONTAINER_DUMP_PATH/$DUMP_NAME"
        log "Importing into schema '$DB_USER' inside container $ORACLE_CONTAINER_NAME ..."
        run_datapump impdp "$PAR_EXTRA"
        podman exec --user oracle "$ORACLE_CONTAINER_NAME" rm -f "$CONTAINER_DUMP_PATH/$DUMP_NAME" "$CONTAINER_DUMP_PATH/$LOG_NAME"
    else
        ensure_directory_object "$BACKUP_DIR"
        [[ "$(dirname "$DUMP_PATH")" == "$BACKUP_DIR" ]] || cp "$DUMP_PATH" "$BACKUP_DIR/$DUMP_NAME"
        log "Importing into schema '$DB_USER' ..."
        run_datapump impdp "$PAR_EXTRA"
    fi

    log "Restore complete. See $BACKUP_DIR/$LOG_NAME for Data Pump's log."
    log "Restart the app so pooled connections pick up the restored data: sudo systemctl restart $SERVICE_NAME"
    ;;

  list)
    ls -lht "$BACKUP_DIR"/aims_*.dmp 2>/dev/null || echo "No backups found in $BACKUP_DIR"
    ;;

  *)
    die "Unknown subcommand '$SUBCOMMAND'. Usage: $0 {backup|restore <file>|list} [--yes]"
    ;;
esac
