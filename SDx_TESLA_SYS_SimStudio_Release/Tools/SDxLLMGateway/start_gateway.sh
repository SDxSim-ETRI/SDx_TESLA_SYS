#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
# Old API credentials are not needed and are not passed to this process.
unset OPENAI_API_KEY OPENAI_MODEL SDX_REASONING_EFFORT OLLAMA_API_KEY
export OLLAMA_NO_CLOUD=1
exec /usr/bin/python3 server.py "$@"
