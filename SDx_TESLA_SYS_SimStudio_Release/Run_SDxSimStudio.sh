#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
APP="$APP_DIR/SDxSimStudio_Linux.x86_64"
if [[ ! -f "$APP" || ! -d "$APP_DIR/SDxSimStudio_Linux_Data" || ! -f "$APP_DIR/UnityPlayer.so" ]]; then
    printf 'ERROR: Incomplete build in %s\n' "$APP_DIR" >&2
    exit 1
fi
if [[ ! -x "$APP" ]]; then
    printf 'ERROR: Set executable permission first: chmod +x "%s"\n' "$APP" >&2
    exit 1
fi
cd -- "$APP_DIR"
exec "$APP" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 "$@"
