"""Exercise a packaged world worker without fetching provider data or using saves."""
from pathlib import Path
import argparse,json,subprocess,tempfile,time,urllib.request,urllib.error

def check(root):
    root=Path(root).resolve()
    command=([str(root/'Python/python.exe'),'-X','utf8',str(root/'_internal/worker_entry.py')]
             if (root/'Python/python.exe').exists() else [str(root/'EarthUnscriptedWorldWorker')])
    expected=json.loads((root/'_internal/worker-code-identity.json').read_text())['workerIdentity']
    with tempfile.TemporaryDirectory(prefix='EarthUnscriptedWorkerCheck-') as temp:
        folder=Path(temp);session=folder/'session.json'
        session.write_text(json.dumps(dict(latitude=51.59,longitude=-3.15,datum=0,countryCode='GB',
            trafficSide='left',name='Isolated security check',start=dict(x=0,y=0,z=0),headingX=1,headingZ=0)))
        port=0;old_token=None
        for attempt in range(2):
            evidence=folder/('worker-'+str(attempt))
            args=command+['--session',str(session),'--cache',str(folder/'cache'),'--evidence',str(evidence)]
            if port:args+=['--port',str(port)]
            with (folder/('worker-'+str(attempt)+'.log')).open('w') as output:
                process=subprocess.Popen(args,stdout=output,stderr=subprocess.STDOUT)
                try:
                    endpoint=evidence/'endpoint.json';deadline=time.monotonic()+45
                    while not endpoint.exists():
                        if process.poll() is not None:raise AssertionError('Worker stopped before publishing its private endpoint')
                        if time.monotonic()>deadline:raise TimeoutError('Worker endpoint')
                        time.sleep(.1)
                    info=json.loads(endpoint.read_text());port=int(info['url'].rsplit(':',1)[1])
                    def request(path,headers):
                        try:
                            with urllib.request.urlopen(urllib.request.Request(info['url']+path,headers=headers),timeout=10) as reply:
                                return reply.status,reply.read()
                        except urllib.error.HTTPError as error:return error.code,error.read()
                    assert request('/health',{})[0]==403
                    headers={'X-Earth-Session':info['token']}
                    status,data=request('/health',headers);assert status==200 and json.loads(data)['workerIdentity']==expected
                    assert request('/prepared/..\\private.json',headers)[0]==400
                    assert request('/health',dict(headers,Origin='https://example.invalid'))[0]==403
                    assert request('/health',dict(headers,Host='example.invalid'))[0]==403
                    if old_token:
                        assert old_token!=info['token'] and request('/health',{'X-Earth-Session':old_token})[0]==403
                    old_token=info['token']
                finally:
                    if process.poll() is None:process.terminate()
                    process.wait(timeout=15)
    print('Packaged worker: authenticated health, denied anonymous/origin/Host/traversal, rotated recovery session passed')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--worker',type=Path,required=True)
    check(parser.parse_args().worker)
