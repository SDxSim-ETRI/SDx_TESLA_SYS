#!/usr/bin/env python3
"""Local Gateway tests. Never calls Unity and never moves a robot.
--health checks the gateway. --text/--live-eval use the installed local model.
No API key, paid API call, model download, or remote endpoint is permitted.
"""
import argparse
import json
import re
import sys
import time
import uuid
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import Request, build_opener, ProxyHandler
from ollama_backend import NoRedirect
from policy import PROTOCOL, DESIGN_EXAMPLES
from server import config_path

def load_config():
    path=config_path()
    if not path.is_file() or path.stat().st_size>16384:
        raise ValueError('Local gateway config is missing. Enter Unity Play and wait for LLM readiness (manual fallback: start_gateway.sh).')
    config=json.loads(path.read_text())
    endpoint=config.get('endpoint','')
    u=urlsplit(endpoint)
    if (u.scheme!='http' or u.hostname!='127.0.0.1' or
        u.port is None or not 1024<=u.port<=65535 or
        u.path!='/v1/interpret' or u.query or u.fragment or u.username or u.password or
        config.get('protocol_version')!=1 or
        not re.fullmatch(r'[A-Za-z0-9_-]{32,128}',config.get('token',''))):
        raise ValueError('Invalid local gateway config. Restart the gateway.')
    return config

def call(config,text=None):
    headers={'X-SDx-Token':config['token']}
    endpoint=config['endpoint'];data=None
    if text is None:endpoint=endpoint[:-len('/v1/interpret')]+'/health'
    else:
        headers['Content-Type']='application/json'
        data=json.dumps({'protocol_version':PROTOCOL,'request_id':uuid.uuid4().hex,'text':text},ensure_ascii=False).encode()
    timeout=min(600,max(30,config.get('request_timeout_seconds',210)))
    with build_opener(ProxyHandler({}),NoRedirect()).open(Request(endpoint,data=data,headers=headers),timeout=timeout) as response:
        raw=response.read(16385)
        if len(raw)>16384:raise ValueError('Response too large')
        return json.loads(raw)

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    mode=ap.add_mutually_exclusive_group(required=True)
    mode.add_argument('--health',action='store_true');mode.add_argument('--text');mode.add_argument('--live-eval',action='store_true')
    ap.add_argument('--repeat',type=int,default=1);ap.add_argument('--report',type=Path)
    a=ap.parse_args()
    if not 1<=a.repeat<=5:raise ValueError('Repeat must be 1..5')
    config=load_config();health=call(config)
    if health.get('provider')!='ollama-local' or health.get('api_key_required') is not False:
        raise ValueError('An older/non-local gateway is running. Close its terminal, then stop and re-enter Unity Play.')
    if a.health:
        print(json.dumps(health,ensure_ascii=False,indent=2));return 0
    if a.text is not None:
        print(json.dumps(call(config,a.text),ensure_ascii=False,indent=2));return 0
    rows=[]
    for repeat in range(a.repeat):
        for text,expected in DESIGN_EXAMPLES:
            t=time.monotonic();got=call(config,text)
            row={'round':repeat+1,'text':text,'expected':expected,'actual':got.get('decision'),
                 'passed':got.get('decision')==expected,'elapsed_seconds':round(time.monotonic()-t,3),'response':got}
            rows.append(row)
            print(('PASS' if row['passed'] else 'FAIL'),repeat+1,text,'=>',got.get('decision'),got.get('reason_code'),flush=True)
            time.sleep(1.1)
    report={'scope':'LIVE local Ollama classification through Gateway; NO Unity or robot execution',
            'environment':health.get('environment'), 'gateway_version':health.get('version'),
            'passed':sum(r['passed'] for r in rows),'total':len(rows),'cases':rows}
    if a.report:
        a.report.expanduser().write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        print('Report:',a.report.expanduser())
    print('LOCAL LLM EVAL:',report['passed'],'/',report['total'])
    return 0 if all(r['passed'] for r in rows) else 2

if __name__=='__main__':
    try:sys.exit(main())
    except Exception as e:
        print('PROBE FAILED:',type(e).__name__,str(e),file=sys.stderr);sys.exit(1)
