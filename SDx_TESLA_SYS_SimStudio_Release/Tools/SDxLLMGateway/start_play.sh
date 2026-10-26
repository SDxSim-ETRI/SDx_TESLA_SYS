#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
# Unity owns stdin. exec preserves that lease through bash -> Python.
unset OPENAI_API_KEY OPENAI_MODEL SDX_REASONING_EFFORT OLLAMA_API_KEY
export OLLAMA_NO_CLOUD=1
export PYTHONDONTWRITEBYTECODE=1
export PYTHONUNBUFFERED=1
export PYTHONIOENCODING=utf-8
exec /usr/bin/python3 -u play_session.py "$@"
