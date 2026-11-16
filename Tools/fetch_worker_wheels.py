"""Download only the exact wheel filenames and hashes in the reviewed inventory."""
from pathlib import Path
import argparse,hashlib,json,os,urllib.request,urllib.parse,uuid
ROOT=Path(__file__).resolve().parents[1]

def fetch(platform,output):
    pins=json.loads((ROOT/'World'/('windows-wheels.json' if platform=='Windows' else 'macos-wheels.json')).read_text())
    output.mkdir(parents=True,exist_ok=True)
    for name,expected in pins.items():
        if Path(name).name!=name or not name.endswith('.whl'):raise ValueError('Invalid pinned wheel name')
        target=output/name
        if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest()==expected:continue
        package,version=name.split('-')[:2]
        with urllib.request.urlopen('https://pypi.org/pypi/'+package+'/'+version+'/json',timeout=30) as response:
            metadata=response.read(2*1024*1024+1)
        if len(metadata)>2*1024*1024:raise ValueError('Wheel metadata limit')
        rows=[row for row in json.loads(metadata)['urls'] if row['filename']==name and row['digests']['sha256']==expected]
        if len(rows)!=1:raise ValueError('Pinned wheel unavailable: '+name)
        uri=urllib.parse.urlsplit(rows[0]['url'])
        if uri.scheme!='https' or uri.hostname!='files.pythonhosted.org' or uri.username or uri.port not in (None,443):
            raise ValueError('Unapproved wheel download URL')
        temporary=output/(name+'.'+uuid.uuid4().hex+'.partial');digest=hashlib.sha256();count=0
        try:
            with urllib.request.urlopen(rows[0]['url'],timeout=60) as response,temporary.open('xb') as file:
                while chunk:=response.read(65536):
                    count+=len(chunk)
                    if count>256*1024*1024:raise ValueError('Wheel size limit')
                    digest.update(chunk);file.write(chunk)
            if digest.hexdigest()!=expected:raise ValueError('Wheel digest mismatch: '+name)
            os.replace(temporary,target)
        finally:temporary.unlink(missing_ok=True)
        print('Verified '+name,flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('platform',choices=['Mac','Windows']);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();fetch(args.platform,args.output)
