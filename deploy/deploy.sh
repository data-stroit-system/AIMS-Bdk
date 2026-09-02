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
: "${SERVER_NAME:=_}"            # nginx server_name; "_" = catch-all (use a hostname like dtp.devtim.my.id otherwise)
: "${NGINX_LISTEN_PORT:=80}"    # external port nginx listens on when SSL is OFF (site is deployed on 81 in prod — set this in deploy.conf, don't hand-edit the generated nginx site, it gets overwritten every deploy)
: "${NGINX_CLIENT_MAX_BODY_SIZE:=50m}"
# --- TLS (optional) — set NGINX_ENABLE_SSL=true to terminate HTTPS on nginx.
# Intended pairing with Cloudflare in Full (strict) mode using a free
# Cloudflare Origin Certificate (https://dash.cloudflare.com → SSL/TLS →
# Origin Server → Create Certificate). Drop the cert+key at the paths below
# (chmod 600 the key) and deploy.sh will:
#   - listen on 443 with the cert, proxy to Kestrel, forward X-Forwarded-Proto=https
#   - also listen on 80 and 301-redirect to https (set NGINX_REDIRECT_HTTP_TO_HTTPS=false to drop the :80 block entirely)
# Leave NGINX_ENABLE_SSL unset/false to keep the legacy plain-HTTP single-listener behaviour.
: "${NGINX_ENABLE_SSL:=false}"
: "${NGINX_SSL_CERT:=/etc/ssl/cloudflare/dtp.devtim.my.id.pem}"
: "${NGINX_SSL_KEY:=/etc/ssl/cloudflare/dtp.devtim.my.id.key}"
: "${NGINX_REDIRECT_HTTP_TO_HTTPS:=true}"
: "${CSPROJ_PATH:=src/AIMS.WebFrontend/AIMS.WebFrontend.csproj}"
: "${BUILD_CONFIGURATION:=Release}"
: "${QGIS_SERVER_URL:=http://192.168.0.8/qgisserver}"
: "${QGIS_MAP_PROJECT:=/home/deli/OrthoProject1/OrthoProject1.qgs}"
# Browser-facing QGIS URL. When the app is served over HTTPS, browsers block
# the WMS GetCapabilities + tile fetches as "mixed content" if QGIS_SERVER_URL
# is plain http://. nginx proxies /qgisserver → $QGIS_SERVER_URL below, so
# setting this to "/qgisserver" makes the browser talk same-origin HTTPS to
# nginx, which forwards over plain HTTP to the upstream QGIS box internally.
# Leave empty in dev (no nginx in front of `dotnet run` → the browser falls
# back to QGIS_SERVER_URL directly, which is fine because the dev page is
# also plain HTTP, no mixed-content blocking).
: "${QGIS_BROWSER_URL:=/qgisserver}"

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
  "$SHARED_DIR/wwwroot/asset-documents" \
  "$SHARED_DIR/Logs"
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

if ! command -v jq >/dev/null; then
  log "Installing jq ..."
  sudo apt-get update -qq
  sudo apt-get install -y -qq jq
else
  log "jq already installed."
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
sudo chown -R "$APP_USER:$APP_USER" "$SHARED_DIR/wwwroot/asset-pictures" "$SHARED_DIR/wwwroot/asset-documents" "$SHARED_DIR/Logs"

# ---------------------------------------------------------------------------
# 5. Default appsettings.Production.json (never overwritten once present)
# ---------------------------------------------------------------------------
if [[ ! -f "$SHARED_DIR/appsettings.Production.json" ]]; then
  log "Writing default $SHARED_DIR/appsettings.Production.json — EDIT THIS with the real Oracle password, then restart the service."
  sudo tee "$SHARED_DIR/appsettings.Production.json" >/dev/null <<JSON
{
  "QgisServer": {
    "ServerUrl": "${QGIS_SERVER_URL}",
    "MapProject": "${QGIS_MAP_PROJECT}"
  },
  "DatabaseProvider": "Oracle",
  "ConnectionStrings": {
    "Oracle": "Data Source=192.168.0.8:1521/xe;User Id=aims;Password=del123;"
  }
}
JSON
  sudo chown "$APP_USER:$APP_USER" "$SHARED_DIR/appsettings.Production.json"
  sudo chmod 640 "$SHARED_DIR/appsettings.Production.json"
fi

# Upsert QgisServer section on every deploy so deploy.conf changes take
# effect even when appsettings.Production.json already exists.
if command -v jq >/dev/null; then
  log "Upserting QgisServer section in appsettings.Production.json ..."
  sudo cat "$SHARED_DIR/appsettings.Production.json" | jq \
    --arg url "$QGIS_SERVER_URL" \
    --arg project "$QGIS_MAP_PROJECT" \
    --arg browser "$QGIS_BROWSER_URL" \
    '.QgisServer = {"ServerUrl": $url, "MapProject": $project, "BrowserUrl": $browser}' \
    | sudo tee "$SHARED_DIR/appsettings.Production.json.tmp" >/dev/null
  sudo mv "$SHARED_DIR/appsettings.Production.json.tmp" "$SHARED_DIR/appsettings.Production.json"
  sudo chown "$APP_USER:$APP_USER" "$SHARED_DIR/appsettings.Production.json"
  sudo chmod 640 "$SHARED_DIR/appsettings.Production.json"
else
  log "jq not installed — QgisServer section not updated in appsettings.Production.json."
  log "  Install with: sudo apt-get install -y jq"
fi

# ---------------------------------------------------------------------------
# 6. Wire the new release to shared/persistent data, then activate it
# ---------------------------------------------------------------------------
sudo ln -sfn "$SHARED_DIR/wwwroot/asset-pictures"  "$RELEASE_DIR/wwwroot/asset-pictures"
sudo ln -sfn "$SHARED_DIR/wwwroot/asset-documents" "$RELEASE_DIR/wwwroot/asset-documents"
sudo ln -sfn "$SHARED_DIR/appsettings.Production.json" "$RELEASE_DIR/appsettings.Production.json"
sudo ln -sfn "$SHARED_DIR/Logs" "$RELEASE_DIR/Logs"
# copy the dtp* and badak* pictures from repo folder AIMS.WebFrontend wwwroot/asset-pictures  into the new shared folder, so they survive redeploys (they are not in the repo itself)
sudo cp -r "$REPO_ROOT/src/AIMS.WebFrontend/wwwroot/asset-pictures/dtp"* "$SHARED_DIR/wwwroot/asset-pictures/"
sudo cp -r "$REPO_ROOT/src/AIMS.WebFrontend/wwwroot/asset-pictures/badak"* "$SHARED_DIR/wwwroot/asset-pictures/"
sudo cp -r "$REPO_ROOT/src/AIMS.WebFrontend/wwwroot/asset-pictures/Badak"* "$SHARED_DIR/wwwroot/asset-pictures/"
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

# Cloudflare Origin Certificates are valid for 15 years but only when
# accessed *through* Cloudflare's proxy. For Full (strict) mode that's fine
# — Cloudflare presents its own cert to visitors and validates the origin
# cert only on the CF→origin hop, which is exactly this cert. If you ever
# switch to direct visitor access (grey-cloud), get a public cert (Let's
# Encrypt) and point NGINX_SSL_CERT/KEY at it instead.

write_proxy_block() {
    # body of a server block — shared by both the plain-HTTP and HTTPS
    # server blocks. $http_host (not $host) keeps the port so the app's
    # absolute redirects (e.g. the cookie auth challenge to /Account/Login)
    # come back on the right port.
    cat <<PROXY
    client_max_body_size $NGINX_CLIENT_MAX_BODY_SIZE;

    # Proxy /qgisserver → upstream QGIS Server so the browser can talk to
    # QGIS same-origin (HTTPS when NGINX_ENABLE_SSL=true, plain HTTP
    # otherwise) instead of hitting http://<upstream> directly — which
    # browsers block as "mixed content" on an HTTPS page. The upstream
    # URL is substituted as a literal (no variable) so nginx resolves it
    # at config-load, no resolver directive needed. Query string is
    # preserved by default.
    location /qgisserver {
        proxy_pass $QGIS_SERVER_URL;
        proxy_set_header Host \$http_host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location / {
        proxy_pass http://127.0.0.1:$APP_PORT;
        proxy_set_header Host \$http_host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        # \$scheme here is whatever THIS server block listens as — https
        # for the :443 block, http for the plain :80 block — so the app's
        # ForwardedHeaders middleware sees the real external scheme and
        # UseHttpsRedirection/UseHsts do the right thing (no redirect loop,
        # because the :80 block 301s at nginx before reaching the app).
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
PROXY
}

if [[ "$NGINX_ENABLE_SSL" == "true" ]]; then
  [[ -f "$NGINX_SSL_CERT" ]] || die "NGINX_ENABLE_SSL=true but cert not found at $NGINX_SSL_CERT. Place the cert there (e.g. a Cloudflare Origin Certificate) and rerun."
  [[ -f "$NGINX_SSL_KEY"  ]] || die "NGINX_ENABLE_SSL=true but key not found at $NGINX_SSL_KEY."

  log "SSL enabled — cert: $NGINX_SSL_CERT"
  # Generate the full site with both :80 (redirect) and :443 (proxy) blocks.
  sudo tee "/etc/nginx/sites-available/${SERVICE_NAME}" >/dev/null <<NGINX
$( if [[ "$NGINX_REDIRECT_HTTP_TO_HTTPS" == "true" ]]; then cat <<HTTP80
server {
    listen 80;
    listen [::]:80;
    server_name $SERVER_NAME;

    # Hand off any plain-HTTP request to HTTPS before it reaches the app,
    # so UseHttpsRedirection never fires on a :80 request (it would
    # otherwise 307 to https://... which works, but doing it here saves
    # a round-trip through Kestrel).
    return 301 https://\$host\$request_uri;
}
HTTP80
fi )
server {
    listen 443 ssl;
    listen [::]:443 ssl;
    server_name $SERVER_NAME;

    ssl_certificate     $NGINX_SSL_CERT;
    ssl_certificate_key $NGINX_SSL_KEY;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_prefer_server_ciphers off;
    # HSTS at the nginx edge too — doubles up on the app-level UseHsts so
    # even a static-file response (which bypasses the app middleware
    # pipeline via nginx) carries the header.
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;

$( write_proxy_block )
}
NGINX
else
  # Legacy plain-HTTP single-listener behaviour (current prod on :81).
  sudo tee "/etc/nginx/sites-available/${SERVICE_NAME}" >/dev/null <<NGINX
server {
    listen $NGINX_LISTEN_PORT;
    listen [::]:$NGINX_LISTEN_PORT;
    server_name $SERVER_NAME;

$( write_proxy_block )
}
NGINX
fi
sudo ln -sfn "/etc/nginx/sites-available/${SERVICE_NAME}" "/etc/nginx/sites-enabled/${SERVICE_NAME}"
[[ -e /etc/nginx/sites-enabled/default ]] && sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl reload nginx

# ---------------------------------------------------------------------------
# 9. Firewall (best-effort, only if ufw is active)
# ---------------------------------------------------------------------------
if command -v ufw >/dev/null && sudo ufw status | grep -q "Status: active"; then
  if [[ "$NGINX_ENABLE_SSL" == "true" ]]; then
    # Cloudflare proxied traffic still arrives on 443 (and 80 for the
    # redirect); open both. Cloudflare's source IPs are documented at
    # https://www.cloudflare.com/ips/ if you want to lock this down further.
    sudo ufw allow 443/tcp >/dev/null || true
    [[ "$NGINX_REDIRECT_HTTP_TO_HTTPS" == "true" ]] && sudo ufw allow 80/tcp >/dev/null || true
  else
    sudo ufw allow "$NGINX_LISTEN_PORT/tcp" >/dev/null || true
  fi
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
HEALTH_URL="http://127.0.0.1:$APP_PORT/Account/Login"
if [[ "$NGINX_ENABLE_SSL" == "true" ]] && [[ "$SERVER_NAME" != "_" ]] && [[ "$SERVER_NAME" != "" ]]; then
  # Also probe the public HTTPS endpoint to confirm the cert + proxy
  # path end-to-end. Use --resolve so we hit 127.0.0.1 without DNS.
  PUBLIC_URL="https://${SERVER_NAME}/Account/Login"
fi
for i in $(seq 1 10); do
  if curl -fsS -o /dev/null "$HEALTH_URL"; then
    log "Service is up (local Kestrel)."
    if [[ -n "${PUBLIC_URL:-}" ]]; then
      if curl -fsS --resolve "${SERVER_NAME}:443:127.0.0.1" -o /dev/null "$PUBLIC_URL"; then
        log "Public HTTPS endpoint OK — $PUBLIC_URL"
      else
        echo "WARNING: local Kestrel is up but $PUBLIC_URL did not respond 2xx (check nginx SSL cert / server_name)." >&2
      fi
    fi
    log "Deployed release $RELEASE_ID."
    exit 0
  fi
  sleep 1
done
echo "WARNING: health check did not get a response from $HEALTH_URL" >&2
echo "Check: sudo systemctl status $SERVICE_NAME  &&  sudo journalctl -u $SERVICE_NAME -n 100 --no-pager" >&2
exit 1
