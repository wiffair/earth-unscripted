"""Geographic capability selection, distinct from feed parsing or gameplay."""
import json
from pathlib import Path
from urllib.parse import urlsplit,parse_qsl
from .model import point,digest

ALIASES={'UK':'GB','GB-ENG':'GB','GB-WLS':'GB','GB-SCT':'GB','GB-NIR':'GB'}

def country_code(code):return ALIASES.get(str(code).upper(),str(code).upper())

def contains(bounds,lon,lat):
    west,south,east,north=bounds
    return south<=lat<=north and (west<=lon<=east if west<=east else lon>=west or lon<=east)

class Catalogue:
    def __init__(self,path=None):
        self.data=json.loads((Path(path) if path else Path(__file__).with_name('catalogue.json')).read_text())
        if self.data.get('schema')!=1:raise ValueError('Unsupported provider catalogue')
        self.providers=self.data['providers'];self.identity=digest(self.data)
        if len({p['id'] for p in self.providers})!=len(self.providers):raise ValueError('Duplicate provider ID')
        for p in self.providers:
            if len(p['bounds'])!=4 or not p['capabilities']:raise ValueError('Missing provider coverage/capability')
            for url in p.get('endpoints',[]):
                parsed=urlsplit(url)
                if parsed.scheme!='https' or parsed.username or parsed.password or any(k.lower() in {'key','token','apikey','api_key','password'} for k,_ in parse_qsl(parsed.query)):
                    raise ValueError('Catalogue endpoints cannot contain credentials or insecure transport')
    def resolve(self,lon,lat,country,capability,credentials=(),unavailable=(),date=None):
        point(lon,lat);country=country_code(country);rows=[]
        for p in self.providers:
            if capability not in p['capabilities'] or not contains(p['bounds'],lon,lat):continue
            if p.get('countries') and country not in p['countries']:continue
            reason='ready'
            if p['status']!='active':reason=p['status']
            elif p['id'] in unavailable:reason='temporarily unavailable'
            elif p.get('credentialRef') and p['credentialRef'] not in credentials:reason='credentials required'
            elif date and (date<p.get('validFrom','0000') or date>p.get('validUntil','9999')):reason='outside validity dates'
            rows.append(dict(provider=p,available=reason=='ready',reason=reason))
        return sorted(rows,key=lambda r:(not r['available'],-r['provider']['priority'],r['provider']['id']))
    def terrain_binding(self,lon,lat,country):
        selected=next((r['provider'] for r in self.resolve(lon,lat,country,'terrain') if r['available']),None)
        if selected is None:raise ValueError('No terrain provider available at this location')
        return selected['binding'],selected['nativeEPSG'],selected['id']
