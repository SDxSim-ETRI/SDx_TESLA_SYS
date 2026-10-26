#!/usr/bin/env bash
# Run explicitly after reviewing. Changes only this package's systemd drop-in.
set -euo pipefail
if [ "${EUID:-$(id -u)}" -ne 0 ]; then
  echo "Run: sudo bash \"$0\"" >&2
  exit 1
fi
command -v systemctl >/dev/null || { echo "systemd is required; see README manual serve instructions." >&2; exit 1; }
systemctl cat ollama.service >/dev/null || { echo "Install Ollama before running this script." >&2; exit 1; }
dropin=/etc/systemd/system/ollama.service.d/90-sdx-local.conf
if [ -L "$dropin" ] || [ -L /etc/systemd/system/ollama.service.d ]; then
  echo "Refusing a symlinked configuration path." >&2; exit 1
fi
mkdir -p /etc/systemd/system/ollama.service.d
if [ -f "$dropin" ]; then
  backup="/var/backups/sdx-ollama-$(date +%Y%m%d_%H%M%S)-$$.conf"
  cp -p "$dropin" "$backup"
  echo "Prior SDx drop-in saved to: $backup"
fi
cat > "$dropin" <<'EOF'
[Service]
Environment="OLLAMA_HOST=127.0.0.1:11434"
Environment="OLLAMA_NO_CLOUD=1"
Environment="OLLAMA_NUM_PARALLEL=1"
Environment="OLLAMA_MAX_LOADED_MODELS=1"
EOF
chmod 644 "$dropin"
systemctl daemon-reload
systemctl enable ollama.service
systemctl restart ollama.service
systemctl is-active --quiet ollama.service
printf '%s\n' "LOCAL CONFIG APPLIED: $dropin" "Cloud features disabled; API listens on loopback only." "Inspect effective configuration: systemctl cat ollama.service"
