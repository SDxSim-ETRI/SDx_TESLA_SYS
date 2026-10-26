#!/usr/bin/env python3
"""Check loopback Ollama metadata only. No model generation, pull, or robot action."""
import json
import sys
from ollama_backend import from_environment, ProviderError

def main():
    try:
        c=from_environment()
        info=c.check_environment()
        info.update(provider='ollama-local',endpoint=c.base_url,api_key_required=False,
                    inference_tested=False,keep_alive_seconds=c.keep_alive,
                    timeout_seconds=c.timeout)
        print(json.dumps(info,ensure_ascii=False,indent=2))
        print('OLLAMA ENVIRONMENT OK (metadata only; run probe --live-eval next)')
        return 0
    except (ProviderError,ValueError) as exc:
        print('ENVIRONMENT CHECK FAILED:',str(exc),file=sys.stderr)
        print('Check: systemctl status ollama; ollama list; ollama pull qwen3:4b',file=sys.stderr)
        return 1
if __name__=='__main__':sys.exit(main())
