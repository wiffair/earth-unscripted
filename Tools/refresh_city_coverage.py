"""Refresh the bounded, non-authoritative BBBike city coverage shortlist.

The live directory and exact .poly still gate every runtime PBF download.
Requires the pinned worker Python dependencies. No PBF/map data is downloaded.
"""
from pathlib import Path
import argparse,datetime,hashlib,json,sys,tempfile
from concurrent.futures import ThreadPoolExecutor
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'World/Source'))
from city_extract import BASE,download,names,parse_poly

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache',type=Path,default=Path(tempfile.gettempdir())/'EarthUnscriptedCityCoverageMetadata')
    args=parser.parse_args();args.cache.mkdir(parents=True,exist_ok=True)
    index=args.cache/'index.html';download(index,BASE,256*1024,8);catalogue=names(index.read_bytes())
    def collect(name):
        url=BASE+name+'/'+name+'.poly';path=args.cache/(name+'.poly')
        try:
            receipt=download(path,url,1024*1024,8);bounds=list(parse_poly(path.read_bytes()).bounds)
            return name,dict(bounds=bounds,url=url,sha256=receipt['sha256'])
        except (OSError,ValueError) as error:return name,dict(error=str(error))
    rows=dict(ThreadPoolExecutor(max_workers=2).map(collect,catalogue));valid={n:r for n,r in rows.items() if 'bounds' in r}
    if len(valid)<min(100,len(catalogue)):
        from collections import Counter
        print(Counter(r.get('error') for r in rows.values() if 'error' in r),flush=True)
        raise ValueError('Too few verified city boundaries; existing snapshot retained')
    generated=datetime.datetime.now(datetime.timezone.utc).isoformat();bounds={n:r['bounds'] for n,r in sorted(valid.items())}
    code='"""Generated coverage shortlist; exact live polygons remain authoritative.\nODbL-1.0 map derivatives; https://download.bbbike.org/osm/bbbike/\nRegenerate with Tools/refresh_city_coverage.py.\n"""\nGENERATED_UTC = '+repr(generated)+'\nCOVERAGE = '+repr(bounds)+'\n'
    (ROOT/'World/Source/city_coverage.py').write_text(code)
    provenance=ROOT/'Provenance/WorldData/BBBikeCityCoverage.json';provenance.parent.mkdir(parents=True,exist_ok=True)
    provenance.write_text(json.dumps(dict(schema=1,generatedUTC=generated,source=BASE,licence='ODbL-1.0',attribution='OpenStreetMap contributors; city extraction boundaries supplied by BBBike',purpose='Shortlist only; runtime checks current catalogue and full exact polygon before PBF download',directorySHA256=hashlib.sha256(index.read_bytes()).hexdigest(),cities=valid,omitted={n:r for n,r in rows.items() if 'error' in r}),indent=2)+'\n')
    print(json.dumps(dict(boundaries=len(valid),catalogue=len(catalogue),metadataBytes=sum(p.stat().st_size for p in args.cache.glob('*.poly')),snapshotBytes=len(code),omitted=len(rows)-len(valid))))
if __name__=='__main__':main()
