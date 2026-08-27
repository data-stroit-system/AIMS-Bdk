#!/usr/bin/env bash
#
# Zips this repo's source and scp's it to the target Ubuntu server, then
# extracts it there — so deploy/deploy.sh (which runs ON the server, see
# its header) has something to publish from.
#
# Run this FROM YOUR DEV MACHINE.
#
# Uses `python3 -m zipfile` instead of the zip/unzip CLIs, since neither
# is guaranteed to be installed locally or on the server (see CLAUDE.md's
# note on extracting .pptx files the same way).
#
# Usage:
#   cp deploy/deploy.conf.example deploy/deploy.conf   # fill in REMOTE_HOST etc
#   ./deploy/upload.sh
#
# Requirements: local python3, ssh, scp.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CONF_FILE="$SCRIPT_DIR/deploy.conf"

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

[[ -f "$CONF_FILE" ]] || die "Missing $CONF_FILE — copy deploy.conf.example to deploy.conf and fill it in first."
# shellcheck source=/dev/null
source "$CONF_FILE"

: "${REMOTE_HOST:?REMOTE_HOST not set in deploy.conf (e.g. deli@192.168.0.8)}"
: "${REMOTE_SSH_PORT:=22}"
: "${REMOTE_SSH_KEY:=}"
: "${REMOTE_DIR:=AIMS-Bdk}"   # path on the server (relative to home, or absolute) to extract into

command -v python3 >/dev/null || die "python3 not found locally."
command -v scp      >/dev/null || die "scp not found locally."

SSH_OPTS=(-p "$REMOTE_SSH_PORT" -o StrictHostKeyChecking=accept-new)
[[ -n "$REMOTE_SSH_KEY" ]] && SSH_OPTS+=(-i "$REMOTE_SSH_KEY")

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
ZIP_NAME="aims-source-$(date -u +%Y%m%d%H%M%S).zip"
# Create the zip OUTSIDE the repo root: the walk below zips $REPO_ROOT, and a
# zip created inside it gets added to itself -> runaway growth (hit 9.8G once).
ZIP_PATH="$WORK_DIR/$ZIP_NAME"

log "Zipping $REPO_ROOT -> $ZIP_PATH..."
python3 - "$REPO_ROOT" "$ZIP_PATH" <<'PY'
import os
import sys
import zipfile

repo_root, zip_path = sys.argv[1], sys.argv[2]

EXCLUDE_DIRS = {".git", ".cloud", ".github", "bin", "obj", ".vs", ".vscode",
                 ".idea", "node_modules", "asset-documents"}
EXCLUDE_FILES = {"deploy.conf"}
EXCLUDE_SUFFIXES = (".log", ".zip")  # never zip zips, incl. any stray old ones in the tree

count = 0
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
    for root, dirs, files in os.walk(repo_root):
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]
        for name in files:
            if name in EXCLUDE_FILES or name.endswith(EXCLUDE_SUFFIXES):
                continue
            full = os.path.join(root, name)
            zf.write(full, os.path.relpath(full, repo_root))
            count += 1

print(f"Zipped {count} files.")
PY

ZIP_SIZE="$(du -h "$ZIP_PATH" | cut -f1)"
log "Uploading $ZIP_PATH ($ZIP_SIZE) to $REMOTE_HOST:/home/deli/$ZIP_NAME ..."
scp "-r" "$ZIP_PATH" "$REMOTE_HOST:/home/deli/$ZIP_NAME"

log "Extracting into $REMOTE_DIR on $REMOTE_HOST ..."
ssh "${SSH_OPTS[@]}" "$REMOTE_HOST" \
  "rm -rf '$REMOTE_DIR' && "\
  "mkdir -p '$REMOTE_DIR' && python3 -m zipfile -e ~/$ZIP_NAME '$REMOTE_DIR' && rm -f ~/$ZIP_NAME"


rm -f "$ZIP_PATH"
log "Done. On the server: cd $REMOTE_DIR && ./deploy/deploy.sh"

