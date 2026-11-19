"""Cancellable numeric solve: arrays only, no executable cache/pickle payloads."""
from pathlib import Path
from types import SimpleNamespace
import json,os,subprocess,sys,tempfile,threading,time,zipfile
import numpy as np
from scipy.optimize import linprog
from scipy.sparse import csr_matrix

_cancel=_active=None

def configure(cancel=None,active=None):
    global _cancel,_active
    _cancel,_active=cancel,active

def install():
    import profile_safety
    profile_safety.linprog=solve

def solve(c,*,A_ub,b_ub,bounds,method='highs',options=None):
    if _cancel is None:return linprog(c,A_ub=A_ub,b_ub=b_ub,bounds=bounds,method=method,options=options)
    with tempfile.TemporaryDirectory(prefix='EarthUnscriptedProfileSolve-') as name:
        root=Path(name);matrix=A_ub.tocsr()
        limits=np.array([[(-np.inf if a is None else a),(np.inf if b is None else b)] for a,b in bounds],dtype=float)
        np.savez(root/'input.npz',c=c,data=matrix.data,indices=matrix.indices,indptr=matrix.indptr,shape=matrix.shape,b=b_ub,bounds=limits)
        (root/'options.json').write_text(json.dumps(dict(method=method,options=options or {})))
        command=[sys.executable,'--profile-solve',name] if getattr(sys,'frozen',False) else [sys.executable,str(Path(__file__).resolve()),name]
        process=None;sent=time.monotonic();began=sent;cpu=0.
        try:
            _cancel()
            process=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            while process.poll() is None:
                _cancel()
                if time.monotonic()-began>210:raise TimeoutError("Numeric profile worker exceeded its bounded lifetime")
                if time.monotonic()-sent>=5:
                    try:
                        current=float(json.loads((root/'activity.json').read_text())['cpuSeconds'])
                        if current>cpu+.05 and _active is not None:_active(current-cpu)
                        cpu=current;sent=time.monotonic()
                    except (OSError,ValueError,KeyError):pass
                time.sleep(.1)
            _cancel()
            if process.returncode or not (root/'result.json').exists():raise RuntimeError('Numeric profile worker stopped without a result')
            result=json.loads((root/'result.json').read_text());result['x']=np.load(root/'solution.npy',allow_pickle=False) if result['success'] else None
            if result['success'] and (result['x'].shape!=(len(c),) or not np.isfinite(result['x']).all()):raise ValueError('Invalid numeric profile result')
            return SimpleNamespace(**result)
        finally:
            if process is not None and process.poll() is None:
                process.terminate()
                try:process.wait(timeout=2)
                except subprocess.TimeoutExpired:process.kill();process.wait(timeout=2)
            if process is not None and process.stdin is not None:process.stdin.close()


def main(arguments=None):
    args=sys.argv[1:] if arguments is None else arguments
    if len(args)!=1:raise ValueError('One private numeric job directory required')
    root=Path(args[0]).resolve()
    if root.parent!=Path(tempfile.gettempdir()).resolve() or not root.name.startswith('EarthUnscriptedProfileSolve-'):raise ValueError('Private numeric job directory required')
    with zipfile.ZipFile(root/'input.npz') as archive:
        if sum(item.file_size for item in archive.infolist())>512*1024*1024:raise ValueError('Numeric model exceeds bounded size')
    with np.load(root/'input.npz',allow_pickle=False) as values:
        shape=tuple(int(n) for n in values['shape']);c=values['c'];b=values['b'];bounds=values['bounds']
        if len(shape)!=2 or shape[0]>4_000_000 or shape[1]>1_000_000 or shape!=(len(b),len(c)):raise ValueError('Invalid numeric model dimensions')
        matrix=csr_matrix((values['data'],values['indices'],values['indptr']),shape=shape)
    metadata=json.loads((root/'options.json').read_text())
    if metadata['method']!='highs' or set(metadata['options'])-{'time_limit','primal_feasibility_tolerance','dual_feasibility_tolerance'}:raise ValueError('Unsupported numeric solver options')
    stopped=threading.Event()
    # The parent owns the pipe writer. EOF also detects a killed/crashed parent
    # on Windows and Unix, without signalling or inspecting other processes.
    def parent_closed():
        try:closed=os.read(sys.stdin.fileno(),1)==b''
        except (OSError,AttributeError):return
        if closed:os._exit(3)
    threading.Thread(target=parent_closed,daemon=True).start()
    def heartbeat():
        while not stopped.wait(.5):
            temporary=root/'activity.partial';temporary.write_text(json.dumps(dict(cpuSeconds=time.process_time())));os.replace(temporary,root/'activity.json')
    thread=threading.Thread(target=heartbeat,daemon=True);thread.start()
    try:
        result=linprog(c,A_ub=matrix,b_ub=b,bounds=bounds,method='highs',options=metadata['options'])
        if result.success:np.save(root/'solution.npy',result.x,allow_pickle=False)
        (root/'result.json').write_text(json.dumps(dict(success=bool(result.success),status=int(result.status),message=str(result.message))))
    finally:stopped.set();thread.join(timeout=1)

if __name__=='__main__':main()
