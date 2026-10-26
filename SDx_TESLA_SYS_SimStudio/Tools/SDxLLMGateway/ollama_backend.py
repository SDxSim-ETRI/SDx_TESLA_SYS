"""Local-only Ollama adapter. Python 3.10+, standard library. No cloud fallback.
Every network request is to a validated numeric loopback URL; proxies and redirects
are disabled. Model weights must already exist; this module never pulls models.
"""
from __future__ import annotations
import json
import os
import socket
from urllib.parse import urlsplit
from urllib.request import Request, build_opener, ProxyHandler, HTTPRedirectHandler
from urllib.error import HTTPError, URLError
from policy import INSTRUCTIONS, SCHEMA, strict_json

VERSION = '2.1.0-play'
MODEL_DEFAULT = 'qwen3:4b'
ALLOWED_MODELS = ('qwen3:1.7b', 'qwen3:4b')
MAX_BODY = 2 * 1024 * 1024

class ProviderError(RuntimeError):
    def __init__(self, code: str):
        self.code = code
        super().__init__(code)

class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise ProviderError('ollama_redirect_blocked')

def local_url(value: str) -> str:
    try:
        u = urlsplit(value)
        port = u.port
    except ValueError:
        raise ValueError('Invalid Ollama URL') from None
    if (u.scheme != 'http' or u.hostname != '127.0.0.1' or
        u.username is not None or u.password is not None or
        u.path not in ('', '/') or u.query or u.fragment or
        port is None or not 1024 <= port <= 65535):
        raise ValueError('Ollama URL must be http://127.0.0.1:PORT (no path, credentials, proxy or remote host).')
    return 'http://127.0.0.1:' + str(port)

class OllamaClassifier:
    def __init__(self, model=MODEL_DEFAULT, base_url='http://127.0.0.1:11434',
                 timeout=180.0, keep_alive=0, num_ctx=8192, num_predict=768,
                 num_thread=0, opener=None):
        if model not in ALLOWED_MODELS:
            raise ValueError('Use a supported LOCAL model: ' + ', '.join(ALLOWED_MODELS))
        if not 10 <= float(timeout) <= 540:
            raise ValueError('Ollama timeout must be 10..540 seconds')
        if not 4096 <= int(num_ctx) <= 16384:
            raise ValueError('Context length must be 4096..16384')
        if not 384 <= int(num_predict) <= 1536:
            raise ValueError('Output token limit must be 384..1536')
        if not isinstance(keep_alive, int) or not 0 <= keep_alive <= 300:
            raise ValueError('Keep-alive must be 0..300 seconds')
        if not 0 <= int(num_thread) <= 32:
            raise ValueError('CPU threads must be 0..32 (0 = Ollama default)')
        self.model = model
        self.base_url = local_url(base_url)
        self.timeout = float(timeout)
        self.keep_alive = keep_alive
        self.num_ctx = int(num_ctx)
        self.num_predict = int(num_predict)
        self.num_thread = int(num_thread)
        self.opener = opener or build_opener(ProxyHandler({}), NoRedirect())
        self.environment = {}
        self.last_metrics = {}
        self.successful_inferences = 0

    def request(self, path, payload=None, timeout=None):
        if path not in ('/api/version', '/api/tags', '/api/show', '/api/chat'):
            raise ValueError('Endpoint is not allowlisted')
        raw = None if payload is None else json.dumps(payload, ensure_ascii=False).encode('utf-8')
        headers = {'Accept': 'application/json', 'User-Agent': 'SDx-Local-Ollama/' + VERSION}
        if raw is not None:
            headers['Content-Type'] = 'application/json'
        req = Request(self.base_url + path, data=raw, headers=headers,
                      method='GET' if raw is None else 'POST')
        try:
            with self.opener.open(req, timeout=timeout or self.timeout) as response:
                blob = response.read(MAX_BODY + 1)
                if len(blob) > MAX_BODY:
                    raise ProviderError('ollama_response_too_large')
                data = strict_json(blob.decode('utf-8'))
        except HTTPError as exc:
            # Do not echo raw request text, headers or server response bodies.
            raise ProviderError('ollama_http_' + str(exc.code)) from None
        except (TimeoutError, socket.timeout):
            raise ProviderError('ollama_timeout') from None
        except URLError as exc:
            if isinstance(exc.reason, (TimeoutError, socket.timeout)):
                raise ProviderError('ollama_timeout') from None
            raise ProviderError('ollama_unavailable') from None
        except OSError:
            raise ProviderError('ollama_unavailable') from None
        except (UnicodeError, ValueError):
            raise ProviderError('ollama_invalid_json') from None
        if not isinstance(data, dict):
            raise ProviderError('ollama_invalid_response')
        if data.get('error'):
            raise ProviderError('ollama_reported_error')
        return data

    def check_environment(self):
        version = self.request('/api/version', timeout=8)
        tags = self.request('/api/tags', timeout=8)
        models = tags.get('models')
        if not isinstance(models, list):
            raise ProviderError('ollama_invalid_model_list')
        item = next((x for x in models if isinstance(x, dict) and
                     self.model in (x.get('name'), x.get('model'))), None)
        if item is None:
            raise ProviderError('ollama_model_not_installed')
        details = self.request('/api/show', {'model': self.model, 'verbose': False}, timeout=12)
        for source in (item, details):
            if source.get('remote_host') or source.get('remote_model'):
                raise ProviderError('ollama_cloud_model_blocked')
        info = details.get('details')
        if not isinstance(info, dict) or info.get('format') != 'gguf' or info.get('family') != 'qwen3':
            raise ProviderError('ollama_model_format_not_supported')
        if 'completion' not in details.get('capabilities', []):
            raise ProviderError('ollama_completion_not_supported')
        digest = item.get('digest')
        if not isinstance(digest, str) or not digest:
            raise ProviderError('ollama_missing_model_digest')
        self.environment = {
            'ollama_version': str(version.get('version', 'unknown')),
            'model': self.model, 'model_digest': digest,
            'local_model_verified': True,
            'size_bytes': item.get('size'),
            'parameter_size': info.get('parameter_size'),
            'quantization_level': info.get('quantization_level'),
        }
        return dict(self.environment)

    def make_payload(self, text):
        # Schema + non-thinking mode; no tools, images, history or role injection.
        # The system instruction is fixed. User text occupies ONLY the user role.
        options = {'temperature': 0, 'seed': 42, 'num_ctx': self.num_ctx,
                   'num_predict': self.num_predict}
        if self.num_thread:
            options['num_thread'] = self.num_thread
        return {'model': self.model, 'stream': False, 'think': False,
                'format': SCHEMA,
                'messages': [
                    {'role': 'system', 'content': INSTRUCTIONS + '\nJSON schema: ' + json.dumps(SCHEMA, ensure_ascii=False, separators=(',', ':'))},
                    {'role': 'user', 'content': text}],
                'options': options, 'keep_alive': self.keep_alive}

    @staticmethod
    def extract(data):
        if not isinstance(data, dict) or data.get('done') is not True:
            raise ProviderError('ollama_incomplete')
        if data.get('done_reason') not in ('stop', None):
            raise ProviderError('ollama_output_truncated')
        message = data.get('message')
        if not isinstance(message, dict) or message.get('role') != 'assistant':
            raise ProviderError('ollama_missing_message')
        if message.get('tool_calls'):
            raise ProviderError('ollama_tool_call_blocked')
        content = message.get('content')
        if not isinstance(content, str) or not content.strip():
            raise ProviderError('ollama_empty_output')
        # Do not salvage arbitrary prose, code fences, incomplete JSON or reasoning.
        try:
            value = strict_json(content)
        except (ValueError, TypeError):
            raise ProviderError('ollama_invalid_output_json') from None
        if not isinstance(value, dict):
            raise ProviderError('ollama_invalid_output_shape')
        return value

    def classify(self, text):
        # Metadata verification never performs inference or pulls a missing model.
        self.check_environment()
        data = self.request('/api/chat', self.make_payload(text))
        if data.get('model') != self.model:
            raise ProviderError('ollama_response_model_mismatch')
        value = self.extract(data)
        self.last_metrics = {k: data.get(k) for k in
            ('total_duration', 'load_duration', 'prompt_eval_count', 'eval_count', 'eval_duration')}
        self.successful_inferences += 1
        return value


def from_environment():
    return OllamaClassifier(
        model=os.environ.get('SDX_OLLAMA_MODEL', MODEL_DEFAULT),
        base_url=os.environ.get('SDX_OLLAMA_URL', 'http://127.0.0.1:11434'),
        timeout=float(os.environ.get('SDX_OLLAMA_TIMEOUT', '180')),
        keep_alive=int(os.environ.get('SDX_OLLAMA_KEEP_ALIVE', '0')),
        num_ctx=int(os.environ.get('SDX_OLLAMA_CONTEXT', '8192')),
        num_thread=int(os.environ.get('SDX_OLLAMA_THREADS', '0')))
