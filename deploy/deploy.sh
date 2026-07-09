#!/usr/bin/env bash
#
# Publish and deploy AIMS.WebFrontend directly on the target Ubuntu
# server, where nginx runs as a reverse proxy (plain HTTP) in front of
# Kestrel, and Oracle is installed locally on that same server.
#
# Run this ON THE SERVER, in an SSH session, from a checkout of this repo
# (assumes the source code is already there, e.g. via git pull). It:
#   1. dotnet-publishes the app (Release) into a new timestamped release
#      dir under $BASE_DIR/releases/
#   2. (idempotent, first run only) installs the ASP.NET Core runtime and
#      nginx, creates a system user, and writes a default
#      appsettings.Production.json
#   3. symlinks persistent data (appsettings.Production.json,
#      wwwroot/asset-pictures, wwwroot/asset-documents) from a shared/
#      dir into the release, so uploads and secrets survive redeploys
#   4. flips $BASE_DIR/current to the new release
#   5. writes/refreshes the systemd unit and nginx site, then restarts
#      the service and reloads nginx
#   6. prunes old releases and runs a health check
#
# Usage (on the server, from the repo root):
#   cp deploy/deploy.conf.example deploy/deploy.conf   # edit if defaults don't fit
#   ./deploy/deploy.sh
#
# Requirements: dotnet SDK on the server (to publish) and sudo rights —
# you'll be prompted for your password interactively as needed.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CONF_FILE="$SCRIPT_DIR/deploy.conf"

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

[[ -f "$CONF_FILE" ]] || log "No $CONF_FILE found — using defaults (copy deploy.conf.example to override)."
# shellcheck source=/dev/null
[[ -f "$CONF_FILE" ]] && source "$CONF_FILE"

: "${APP_USER:=aims}"           # system user the app runs as (created if missing)
: "${BASE_DIR:=/opt/aims}"      # releases/, shared/, current -> live under here
: "${KEEP_RELEASES:=5}"         # how many old releases to retain for rollback
: "${SERVICE_NAME:=aims-webfrontend}"
: "${APP_PORT:=5000}"           # Kestrel listens on 127.0.0.1:$APP_PORT, nginx proxies to it
: "${DOTNET_CHANNEL:=10.0}"     # ASP.NET Core runtime channel to install
: "${SERVER_NAME:=_}"           # nginx server_name; "_" = catch-all
: "${NGINX_CLIENT_MAX_BODY_SIZE:=50m}"
: "${CSPROJ_PATH:=src/AIMS.WebFrontend/AIMS.WebFrontend.csproj}"
: "${BUILD_CONFIGURATION:=Release}"

command -v dotnet >/dev/null || die "dotnet SDK not found on this machine."
[[ $EUID -ne 0 ]] || die "Run this as your normal login user (it uses sudo itself), not as root."

RELEASE_ID="$(date -u +%Y%m%d%H%M%S)"
RELEASE_DIR="$BASE_DIR/releases/$RELEASE_ID"
SHARED_DIR="$BASE_DIR/shared"
CURRENT_LINK="$BASE_DIR/current"

# Cache sudo credentials for the rest of the script (prompts once if needed).
sudo -v

# ---------------------------------------------------------------------------
# 1. Bootstrap base directories (idempotent)
# ---------------------------------------------------------------------------
sudo mkdir -p "$BASE_DIR/releases" \
  "$SHARED_DIR/wwwroot/asset-pictures" \
  "$SHARED_DIR/wwwroot/asset-documents"
sudo chown "$(id -u):$(id -g)" "$BASE_DIR" "$BASE_DIR/releases"

# ---------------------------------------------------------------------------
# 2. Publish straight into the new release directory
# ---------------------------------------------------------------------------
log "Publishing $CSPROJ_PATH ($BUILD_CONFIGURATION) into $RELEASE_DIR ..."
mkdir -p "$RELEASE_DIR"
(
  cd "$REPO_ROOT"
  dotnet publish "$CSPROJ_PATH" \
    -c "$BUILD_CONFIGURATION" \
    -r linux-x64 \
    --self-contained false \
    -o "$RELEASE_DIR" \
    /p:UseAppHost=false
)

# These are persisted across releases via symlinks (below) — remove the
# empty/placeholder copies the publish step creates so they don't shadow
# the shared/ versions.
rm -rf "$RELEASE_DIR/wwwroot/asset-pictures" "$RELEASE_DIR/wwwroot/asset-documents"
rm -f "$RELEASE_DIR/appsettings.Production.json"

# ---------------------------------------------------------------------------
# 3. ASP.NET Core runtime + nginx (idempotent, first run only)
# ---------------------------------------------------------------------------
if ! dotnet --list-runtimes 2>/dev/null | grep -q "Microsoft.AspNetCore.App ${DOTNET_CHANNEL}"; then
  log "Installing ASP.NET Core runtime $DOTNET_CHANNEL ..."
  UBUNTU_VERSION="$(lsb_release -rs)"
  TMP_DEB="$(mktemp --suffix=.deb)"
  curl -fsSL "https://packages.microsoft.com/config/ubuntu/${UBUNTU_VERSION}/packages-microsoft-prod.deb" -o "$TMP_DEB"
  sudo dpkg -i "$TMP_DEB" >/dev/null
  rm -f "$TMP_DEB"
  sudo apt-get update -qq
  sudo apt-get install -y -qq "aspnetcore-runtime-${DOTNET_CHANNEL}"
else
  log "ASP.NET Core runtime $DOTNET_CHANNEL already installed."
fi

if ! command -v nginx >/dev/null; then
  log "Installing nginx ..."
  sudo apt-get update -qq
  sudo apt-get install -y -qq nginx
else
  log "nginx already installed."
fi

# ---------------------------------------------------------------------------
# 4. System user (idempotent)
# ---------------------------------------------------------------------------
if ! id -u "$APP_USER" >/dev/null 2>&1; then
  log "Creating system user '$APP_USER' ..."
  sudo useradd --system --no-create-home --shell /usr/sbin/nologin "$APP_USER"
fi

# Uploads (asset-pictures, asset-documents) are written at runtime by the
# app process, which runs as $APP_USER (see systemd unit below) — make sure
# it actually owns these dirs, not just root from the mkdir -p above. Runs
# every deploy so it stays correct even if the dirs were created earlier
# under a different owner.
sudo chown -R "$APP_USER:$APP_USER" "$SHARED_DIR/wwwroot/asset-pictures" "$SHARED_DIR/wwwroot/asset-documents"

# ---------------------------------------------------------------------------
# 5. Default appsettings.Production.json (never overwritten once present)
# ---------------------------------------------------------------------------
if [[ ! -f "$SHARED_DIR/appsettings.Production.json" ]]; then
  log "Writing default $SHARED_DIR/appsettings.Production.json — EDIT THIS with the real Oracle password, then restart the service."
  sudo tee "$SHARED_DIR/appsettings.Production.json" >/dev/null <<JSON
{
  "DatabaseProvider": "Oracle",
  "ConnectionStrings": {
    "Oracle": "Data Source=192.168.0.8:1521/xe;User Id=aims;Password=del123;"
  }
}
JSON
  sudo chown "$APP_USER:$APP_USER" "$SHARED_DIR/appsettings.Production.json"
  sudo chmod 640 "$SHARED_DIR/appsettings.Production.json"
fi

# ---------------------------------------------------------------------------
# 6. Wire the new release to shared/persistent data, then activate it
# ---------------------------------------------------------------------------
sudo ln -sfn "$SHARED_DIR/wwwroot/asset-pictures"  "$RELEASE_DIR/wwwroot/asset-pictures"
sudo ln -sfn "$SHARED_DIR/wwwroot/asset-documents" "$RELEASE_DIR/wwwroot/asset-documents"
sudo ln -sfn "$SHARED_DIR/appsettings.Production.json" "$RELEASE_DIR/appsettings.Production.json"
# copy the dtp* and badak* pictures from repo folder AIMS.WebFrontend wwwroot/asset-pictures  into the new shared folder, so they survive redeploys (they are not in the repo itself)
sudo cp -r "$REPO_ROOT/src/AIMS.WebFrontend/wwwroot/asset-pictures/dtp"* "$SHARED_DIR/wwwroot/asset-pictures/"
sudo cp -r "$REPO_ROOT/src/AIMS.WebFrontend/wwwroot/asset-pictures/badak"* "$SHARED_DIR/wwwroot/asset-pictures/"
sudo chown -R "$APP_USER:$APP_USER" "$SHARED_DIR/wwwroot/asset-pictures"

sudo chown -R "$APP_USER:$APP_USER" "$RELEASE_DIR"
sudo ln -sfn "$RELEASE_DIR" "$CURRENT_LINK"

# ---------------------------------------------------------------------------
# 7. systemd unit
# ---------------------------------------------------------------------------
log "Writing systemd unit for $SERVICE_NAME ..."
sudo tee "/etc/systemd/system/${SERVICE_NAME}.service" >/dev/null <<UNIT
[Unit]
Description=AIMS.WebFrontend (SIMS)
After=network.target

[Service]
Type=simple
User=$APP_USER
Group=$APP_USER
WorkingDirectory=$CURRENT_LINK
ExecStart=/usr/bin/dotnet $CURRENT_LINK/AIMS.WebFrontend.dll
Restart=on-failure
RestartSec=5
Environment=TZ=UTC
SyslogIdentifier=$SERVICE_NAME
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:$APP_PORT
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
UNIT

sudo systemctl daemon-reload
sudo systemctl enable "$SERVICE_NAME" >/dev/null
sudo systemctl restart "$SERVICE_NAME"

# ---------------------------------------------------------------------------
# 8. nginx site
# ---------------------------------------------------------------------------
log "Writing nginx site for $SERVICE_NAME ..."
sudo tee "/etc/nginx/sites-available/${SERVICE_NAME}" >/dev/null <<NGINX
server {
    listen 80;
    listen [::]:80;
    server_name $SERVER_NAME;

    client_max_body_size $NGINX_CLIENT_MAX_BODY_SIZE;

    location / {
        proxy_pass http://127.0.0.1:$APP_PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
NGINX
sudo ln -sfn "/etc/nginx/sites-available/${SERVICE_NAME}" "/etc/nginx/sites-enabled/${SERVICE_NAME}"
[[ -e /etc/nginx/sites-enabled/default ]] && sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl reload nginx

# ---------------------------------------------------------------------------
# 9. Firewall (best-effort, only if ufw is active)
# ---------------------------------------------------------------------------
if command -v ufw >/dev/null && sudo ufw status | grep -q "Status: active"; then
  sudo ufw allow 80/tcp >/dev/null || true
fi

# ---------------------------------------------------------------------------
# 10. Prune old releases (never touch the one "current" points to)
# ---------------------------------------------------------------------------
log "Pruning old releases (keeping $KEEP_RELEASES) ..."
CURRENT_TARGET="$(readlink -f "$CURRENT_LINK")"
cd "$BASE_DIR/releases"
ls -1dt */ 2>/dev/null | sed 's#/$##' | tail -n +"$((KEEP_RELEASES + 1))" | while read -r old; do
  old_path="$BASE_DIR/releases/$old"
  [[ "$old_path" == "$CURRENT_TARGET" ]] && continue
  sudo rm -rf "$old_path"
done

# ---------------------------------------------------------------------------
# 11. Health check
# ---------------------------------------------------------------------------
log "Health check ..."
for i in $(seq 1 10); do
  if curl -fsS -o /dev/null "http://127.0.0.1:$APP_PORT/Account/Login"; then
    log "Service is up. Deployed release $RELEASE_ID."
    exit 0
  fi
  sleep 1
done
echo "WARNING: health check did not get a response from http://127.0.0.1:$APP_PORT/Account/Login" >&2
echo "Check: sudo systemctl status $SERVICE_NAME  &&  sudo journalctl -u $SERVICE_NAME -n 100 --no-pager" >&2
exit 1
