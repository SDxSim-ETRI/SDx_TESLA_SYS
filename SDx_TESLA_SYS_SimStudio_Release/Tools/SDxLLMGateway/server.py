#!/usr/bin/env python3
"""Local-only SDx intent gateway. Uses Ollama; no API key or paid/cloud fallback.
The existing Unity protocol and local session-token mechanism are retained.
"""
from __future__ import annotations
import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import secrets
import signal
import sys
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from policy import PROTOCOL, normalize, precheck, result, strict_json, validate_interpretation
from ollama_backend import ProviderError, OllamaClassifier, MODEL_DEFAULT, VERSION
MAX_BODY = 8192

class IntentService:
    def __init__(self,classifier,min_interval=1.0):
        self.classifier=classifier;self.lock=threading.Lock();self.last_call=0.0
        self.min_interval=min_interval;self.recent=[];self.cache={}
    def interpret(self,text,rid):
        text=normalize(text)
        denied=precheck(text,rid)
        if denied:return denied
        if not self.lock.acquire(blocking=False):return result(rid,'busy',decision='error')
        try:
            now=time.monotonic()
            self.cache={k:v for k,v in self.cache.items() if now-v[0]<90}
            digest=hashlib.sha256(text.encode('utf-8')).hexdigest()
            if rid in self.cache:
                entry=self.cache[rid]
                if entry[1]!=digest:return result(rid,'invalid_model_result',decision='error')
                return entry[2]
            self.recent=[t for t in self.recent if now-t<60]
            if now-self.last_call<self.min_interval or len(self.recent)>=60:return result(rid,'busy',decision='error')
            self.last_call=now;self.recent.append(now)
            try:
                raw=self.classifier.classify(text)
                reply=validate_interpretation(text,raw,rid,self.classifier.model)
            except ProviderError as exc:
                print('Provider error:',exc.code,flush=True)
                code=exc.code if exc.code in ('ollama_timeout','ollama_unavailable','ollama_model_not_installed') else 'provider_error'
                reply=result(rid,code,decision='error',source='llm',model=self.classifier.model)
            except Exception:
                # Unexpected malformed provider output must never approve a run.
                print('Provider error: unexpected_response',flush=True)
                reply=result(rid,'invalid_model_result',decision='error',source='llm',model=self.classifier.model)
            if len(self.cache)>=128:self.cache.clear()
            self.cache[rid]=(time.monotonic(),digest,reply)
            return reply
        finally:self.lock.release()

class GatewayServer(ThreadingHTTPServer):
    daemon_threads=True
    def __init__(self,addr,token,service,play_session_id=''):
        self.token=token;self.service=service;self.play_session_id=play_session_id
        super().__init__(addr,Handler)
    def get_request(self):
        sock,addr=super().get_request();sock.settimeout(8);return sock,addr

class Handler(BaseHTTPRequestHandler):
    server_version='SDxIntent/'+VERSION
    def log_message(self,*args):pass
    def send_json(self,code,data):
        body=json.dumps(data,ensure_ascii=False).encode('utf-8')
        try:
            self.send_response(code)
            self.send_header('Content-Type','application/json; charset=utf-8')
            self.send_header('Content-Length',str(len(body)))
            self.send_header('Cache-Control','no-store')
            self.send_header('X-Content-Type-Options','nosniff')
            self.end_headers();self.wfile.write(body)
        except (BrokenPipeError,ConnectionResetError,OSError):pass
    def authorized(self):
        allowed={'127.0.0.1:'+str(self.server.server_port),'localhost:'+str(self.server.server_port)}
        if self.client_address[0]!='127.0.0.1' or self.headers.get('Origin') or self.headers.get('Host') not in allowed:
            self.send_json(403,{'error':'local_client_only'});return False
        if not hmac.compare_digest(self.headers.get('X-SDx-Token',''),self.server.token):
            self.send_json(401,{'error':'invalid_local_token'});return False
        return True
    def do_GET(self):
        if not self.authorized():return
        if self.path!='/health':self.send_json(404,{'error':'not_found'});return
        self.send_json(200,{'protocol_version':PROTOCOL,'status':'ready','version':VERSION,
                            'model':self.server.service.classifier.model,'provider':'ollama-local', 'api_key_required':False,
                            'inference_tested':self.server.service.classifier.successful_inferences > 0,
                            'environment':self.server.service.classifier.environment,
                            'play_session_id':self.server.play_session_id})
    def do_POST(self):
        if not self.authorized():return
        if self.path!='/v1/interpret':self.send_json(404,{'error':'not_found'});return
        if self.headers.get('Transfer-Encoding') or self.headers.get_content_type()!='application/json':
            self.send_json(415,{'error':'json_content_required'});return
        try:
            length=int(self.headers.get('Content-Length','-1'))
            if length<1 or length>MAX_BODY:raise ValueError('body size')
            raw=self.rfile.read(length)
            if len(raw)!=length:raise ValueError('short body')
            body=strict_json(raw.decode('utf-8'))
            if not isinstance(body,dict) or set(body)!={'protocol_version','request_id','text'}:raise ValueError('request shape')
            if type(body['protocol_version']) is not int or body['protocol_version']!=PROTOCOL:raise ValueError('version')
            rid=body['request_id']
            if not isinstance(rid,str) or not re.fullmatch(r'[a-f0-9]{32}',rid):raise ValueError('request id')
            text=body['text']
            if not isinstance(text,str):raise ValueError('text')
        except (ValueError,TypeError,UnicodeError,OSError):
            self.send_json(400,{'error':'invalid_request'});return
        outcome=self.server.service.interpret(text,rid)
        print('Intent:',rid[:8],outcome['decision'],outcome['reason_code'],flush=True)
        self.send_json(200,outcome)
    def do_OPTIONS(self):self.send_json(403,{'error':'browser_clients_not_supported'})


def config_path():
    custom=os.environ.get('SDX_LLM_CONFIG')
    if custom:return Path(custom).expanduser()
    return Path(os.environ.get('XDG_CONFIG_HOME') or Path.home()/'.config')/'sdx-simstudio/intent-gateway.json'

def write_config(path,port,token,request_timeout_seconds=210,play_session_id=''):
    path=Path(path).absolute()
    if path.is_symlink():raise RuntimeError('Refusing a symlinked config file')
    path.parent.mkdir(parents=True,exist_ok=True,mode=0o700)
    if path.parent.is_symlink():raise RuntimeError('Refusing a symlinked config directory')
    fd,tmp=tempfile.mkstemp(prefix='.intent_',dir=str(path.parent))
    try:
        os.fchmod(fd,0o600)
        with os.fdopen(fd,'w',encoding='utf-8') as out:
            json.dump({'protocol_version':PROTOCOL,'endpoint':'http://127.0.0.1:'+str(port)+'/v1/interpret','token':token, 'provider':'ollama-local',
                       'request_timeout_seconds':request_timeout_seconds,'play_session_id':play_session_id},out)
        os.replace(tmp,path)
    finally:
        if os.path.exists(tmp):os.unlink(tmp)
    return path

def main():
    from ollama_backend import from_environment
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port',type=int,default=8765)
    parser.add_argument('--model',default=None)
    parser.add_argument('--play-session-id',default='')
    args=parser.parse_args()
    if not 1024<=args.port<=65535:raise ValueError('Use a port from 1024 to 65535')
    if args.play_session_id and not re.fullmatch(r'[a-f0-9]{32}',args.play_session_id):
        raise ValueError('Invalid Play session identifier')
    if args.model:os.environ['SDX_OLLAMA_MODEL']=args.model
    classifier=from_environment()
    # Bind first. If an older gateway owns the port, do not overwrite its config.
    token=secrets.token_urlsafe(32)
    server=GatewayServer(('127.0.0.1',args.port),token,IntentService(classifier),args.play_session_id)
    config=None
    # handle_request() avoids the serve_forever/shutdown same-thread deadlock.
    stopping=False
    def request_stop(*_):
        nonlocal stopping
        stopping=True
    for signum in (signal.SIGTERM,signal.SIGINT,signal.SIGHUP):
        signal.signal(signum,request_stop)
    server.timeout=.25
    try:
        env=classifier.check_environment()
        if stopping:return 0
        config=write_config(config_path(),args.port,token,int(classifier.timeout)+30,args.play_session_id)
        print('SDx Ollama Gateway READY on 127.0.0.1:'+str(args.port),flush=True)
        print('Local model:',classifier.model,'| Digest:',env['model_digest'],flush=True)
        print('Ollama:',env['ollama_version'],'| Config:',config,flush=True)
        print('No API key. Model installed; run probe --live-eval to check language accuracy.',flush=True)
        print('Ctrl+C stops this gateway, not Ollama or Unity.',flush=True)
        while not stopping:server.handle_request()
    finally:
        server.server_close()
        # Only the matching Play owner may remove its generated token file.
        # Manually started gateways retain their historical config behavior.
        if config is not None and args.play_session_id:
            try:
                current=json.loads(config.read_text(encoding='utf-8'))
                if current.get('token')==token and current.get('play_session_id')==args.play_session_id:
                    config.unlink()
            except (OSError,ValueError,TypeError):pass
    return 0

if __name__=='__main__':
    try:sys.exit(main())
    except ProviderError as ex:
        print('START FAILED:',ex.code,file=sys.stderr)
        print('Run check_environment.py. For a missing model: ollama pull qwen3:4b',file=sys.stderr)
        sys.exit(1)
    except (ValueError,RuntimeError,OSError) as ex:
        print('START FAILED:',str(ex),file=sys.stderr)
        print('Stop the old gateway with Ctrl+C if port 8765 is already in use.',file=sys.stderr)
        sys.exit(1)
