#!/usr/bin/env bash
#
# Installs podman, runs an Oracle XE 11g container, and creates a DB user
# "aims" / "del123" with CONNECT + RESOURCE privileges.
#
# Usage: ./setup_oracle_xe.sh   (run as your normal user, NOT root/sudo)
#
# Podman on Ubuntu runs rootless per-user by default: each user has their own
# container/image storage. Running this whole script under sudo would create
# and run the container as root instead of you, in a separate storage area
# from any rootless container you already have -- and it would collide on
# the host ports if one is already running. Only the package install step
# needs a one-off sudo.
#
set -euo pipefail

CONTAINER_NAME="oracle-xe-11g"
IMAGE="docker.io/oracleinanutshell/oracle-xe-11g:latest"
DB_USER="aims"
DB_PASSWORD="del123"
ORACLE_HOME="/u01/app/oracle/product/11.2.0/xe"
SID="XE"

if [[ $EUID -eq 0 ]]; then
    echo "Do not run this script as root/sudo -- run it as your normal user." >&2
    echo "It will call sudo internally only for the podman package install." >&2
    exit 1
fi

echo "==> Installing podman"
if ! command -v podman >/dev/null 2>&1; then
    sudo apt-get update -y
    sudo apt-get install -y podman
else
    echo "podman already installed: $(podman --version)"
fi

echo "==> Starting ${CONTAINER_NAME} container"
if podman ps -a --format '{{.Names}}' | grep -qx "${CONTAINER_NAME}"; then
    STATUS=$(podman inspect -f '{{.State.Status}}' "${CONTAINER_NAME}")
    if [[ "${STATUS}" != "running" ]]; then
        echo "Container exists but is not running (status: ${STATUS}). Starting it."
        podman start "${CONTAINER_NAME}"
    else
        echo "Container already running."
    fi
else
    podman run -d \
        --name "${CONTAINER_NAME}" \
        -p 1521:1521 \
        -p 8080:8080 \
        "${IMAGE}"
fi

echo "==> Waiting for Oracle listener to accept connections (this can take a few minutes on first start)"
SQLPLUS="podman exec ${CONTAINER_NAME} bash -c \"ORACLE_HOME=${ORACLE_HOME} ORACLE_SID=${SID} LD_LIBRARY_PATH=${ORACLE_HOME}/lib ${ORACLE_HOME}/bin/sqlplus -S -L system/oracle@//localhost:1521/${SID} <<< 'select 1 from dual;'\""

READY=0
for i in $(seq 1 60); do
    eval "${SQLPLUS}" >/tmp/oracle_probe.log 2>&1 || true
    # Any of these indicate the listener + DB are accepting connections,
    # regardless of whether system/oracle happens to be the current password.
    if grep -qE '^1$|ORA-28000|ORA-01017' /tmp/oracle_probe.log; then
        READY=1
        break
    fi
    sleep 10
    echo "  ... still waiting (${i}/60)"
done

if [[ "${READY}" -ne 1 ]]; then
    echo "Timed out waiting for Oracle to become ready. Check container logs:" >&2
    echo "  podman logs ${CONTAINER_NAME}" >&2
    exit 1
fi
echo "Oracle listener is up."

echo "==> Ensuring SYSTEM account is unlocked (via SYSDBA)"
UNLOCK_SQL=$(mktemp)
cat > "${UNLOCK_SQL}" <<'EOF'
ALTER USER SYSTEM ACCOUNT UNLOCK;
exit;
EOF
# podman cp preserves source file mode; mktemp defaults to 600 which the
# container's non-root "oracle" user can't read after cp changes owner to root.
chmod 644 "${UNLOCK_SQL}"
podman cp "${UNLOCK_SQL}" "${CONTAINER_NAME}:/tmp/unlock.sql"
podman exec --user oracle "${CONTAINER_NAME}" bash -c \
    "ORACLE_HOME=${ORACLE_HOME} ORACLE_SID=${SID} LD_LIBRARY_PATH=${ORACLE_HOME}/lib ${ORACLE_HOME}/bin/sqlplus -S / as sysdba @/tmp/unlock.sql"
rm -f "${UNLOCK_SQL}"

echo "==> Creating/ensuring '${DB_USER}' database user"
CREATE_SQL=$(mktemp)
cat > "${CREATE_SQL}" <<EOF
DECLARE
  v_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO v_count FROM dba_users WHERE username = UPPER('${DB_USER}');
  IF v_count = 0 THEN
    EXECUTE IMMEDIATE 'CREATE USER ${DB_USER} IDENTIFIED BY ${DB_PASSWORD}';
  ELSE
    EXECUTE IMMEDIATE 'ALTER USER ${DB_USER} IDENTIFIED BY ${DB_PASSWORD} ACCOUNT UNLOCK';
  END IF;
END;
/
GRANT CONNECT, RESOURCE TO ${DB_USER};
ALTER USER ${DB_USER} QUOTA UNLIMITED ON USERS;
exit;
EOF
chmod 644 "${CREATE_SQL}"
podman cp "${CREATE_SQL}" "${CONTAINER_NAME}:/tmp/create_user.sql"
podman exec --user oracle "${CONTAINER_NAME}" bash -c \
    "ORACLE_HOME=${ORACLE_HOME} ORACLE_SID=${SID} LD_LIBRARY_PATH=${ORACLE_HOME}/lib ${ORACLE_HOME}/bin/sqlplus -S / as sysdba @/tmp/create_user.sql"
rm -f "${CREATE_SQL}"

echo "==> Verifying login as ${DB_USER}"
VERIFY_SQL=$(mktemp)
cat > "${VERIFY_SQL}" <<'EOF'
select 'CONNECT OK' as result from dual;
exit;
EOF
podman cp "${VERIFY_SQL}" "${CONTAINER_NAME}:/tmp/verify.sql"
podman exec "${CONTAINER_NAME}" bash -c \
    "ORACLE_HOME=${ORACLE_HOME} ORACLE_SID=${SID} LD_LIBRARY_PATH=${ORACLE_HOME}/lib ${ORACLE_HOME}/bin/sqlplus -S ${DB_USER}/${DB_PASSWORD}@//localhost:1521/${SID} @/tmp/verify.sql"
rm -f "${VERIFY_SQL}"

echo
echo "==> Done. Connect with:"
echo "    ${DB_USER}/${DB_PASSWORD}@//$(hostname -I | awk '{print $1}'):1521/${SID}"
