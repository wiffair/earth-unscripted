"""Projected coordinate frames for independently acquired driving regions.

All geometry sent to Unity remains local metres. A frame records the source
projection, projected horizontal origin, vertical subtraction and traffic side;
source elevations are retained in their declared datum, without claiming an
unsurveyed conversion between national height systems.
"""
from dataclasses import dataclass,asdict
from functools import lru_cache
from pathlib import Path
import json
import math
import re
from pyproj import Transformer


class GeographicHandoff:
    """Two-dimensional projection through OSM's explicit WGS84 geography.

    Legacy game X/Z use an unlabelled spherical EQC projection. PROJ can choose
    a ballpark datum operation when converting it directly to a national grid,
    moving survey sampling by more than100m. Invert the original game projection
    to WGS84 first, then use the national datum operation. The reverse follows
    the same geographic boundary, preserving existing saved game coordinates.
    No vertical datum conversion is performed. Scalars and NumPy arrays work.
    """
    def __init__(self, source, target):
        self.to_geographic = Transformer.from_crs(source, 4326, always_xy=True)
        self.from_geographic = Transformer.from_crs(4326, target, always_xy=True)

    def transform(self, x, z):
        longitude, latitude = self.to_geographic.transform(x, z)
        return self.from_geographic.transform(longitude, latitude)


@lru_cache(maxsize=64)
def geographic_handoff(source, target):
    """Reuse immutable horizontal transforms; inputs are CRS strings/EPSG IDs."""
    return GeographicHandoff(source, target)


@dataclass(frozen=True)
class RegionFrame:
    identifier: str
    name: str
    epsg: int
    origin_x: float
    origin_z: float
    provider: str
    traffic_side: str='left'
    height_origin: float=100.
    vertical_datum: str='source datum'
    # Optional game coordinate projection. Native source rasters retain epsg;
    # a legacy map's saved X/Z can therefore remain unchanged during rollout.
    world_crs: str|None=None
    country_code: str=''

    @property
    def origin(self):
        return (self.origin_x,self.origin_z)

    @property
    def world_projection(self):
        return self.world_crs or self.epsg

    @property
    def world_origin(self):
        return (0.,0.) if self.world_crs else self.origin

    def identity(self):
        return asdict(self)

    def geographic_bounds(self,bounds):
        transform=Transformer.from_crs(self.world_projection,4326,always_xy=True)
        origin=self.world_origin
        points=[transform.transform(origin[0]+x,origin[1]+z) for x in (bounds[0],bounds[2]) for z in (bounds[1],bounds[3])]
        return [min(p[0] for p in points),min(p[1] for p in points),max(p[0] for p in points),max(p[1] for p in points)]

    def source_bounds(self,bounds):
        """Enclose game-metre bounds in the native raster projection."""
        if not self.world_crs:
            return [bounds[index]+self.origin[index%2] for index in range(4)]
        transform=geographic_handoff(self.world_crs,self.epsg)
        points=[transform.transform(x,z) for x in (bounds[0],bounds[2]) for z in (bounds[1],bounds[3])]
        # One metre covers local projected-edge curvature and pixel rounding.
        return [min(p[0] for p in points)-1,min(p[1] for p in points)-1,
                max(p[0] for p in points)+1,max(p[1] for p in points)+1]

    def world_bounds(self,bounds):
        """Enclose native-raster bounds in game metres, for bounded fallback."""
        if not self.world_crs:
            return [bounds[index]-self.origin[index%2] for index in range(4)]
        transform=geographic_handoff(self.epsg,self.world_crs)
        points=[transform.transform(x,z) for x in (bounds[0],bounds[2]) for z in (bounds[1],bounds[3])]
        return [min(p[0] for p in points),min(p[1] for p in points),max(p[0] for p in points),max(p[1] for p in points)]

    @classmethod
    def legacy_map(cls,identifier,name,latitude,longitude,datum,epsg,provider,traffic_side='left'):
        """Match the main game's existing local coordinate formula exactly.

        Main geographic maps use 111319.4908 metres/degree and cos(origin lat)
        for eastings. PROJ's spherical equidistant cylindrical form reproduces
        that convention while measured rasters stay in their native CRS.
        This is saved-coordinate compatibility, not a surveyed datum conversion.
        """
        if not all(math.isfinite(v) for v in (latitude,longitude,datum)) or abs(latitude)>80 or abs(longitude)>179.98:
            raise ValueError('Invalid legacy geographic origin')
        world=f'+proj=eqc +lat_ts={latitude:.12f} +lat_0={latitude:.12f} +lon_0={longitude:.12f} +R=6378137 +units=m +no_defs +type=crs'
        east,north=Transformer.from_crs(4326,epsg,always_xy=True).transform(longitude,latitude)
        return cls(identifier,name,epsg,east,north,provider,traffic_side,datum,
                   'Native source datum; legacy game vertical origin',world)

    @classmethod
    def load(cls,path=None):
        if path is None:
            return cls('newbridge','South Wales',27700,321000.,197000.,'welsh','left',100.,'Welsh LiDAR source datum, not independently verified')
        result=cls(**json.loads(Path(path).read_text()))
        if result.traffic_side not in ('left','right'):raise ValueError('Invalid traffic side')
        if not result.identifier or not result.identifier.replace('-','').replace('_','').isalnum():raise ValueError('Invalid region identifier')
        if not all(math.isfinite(value) for value in [result.origin_x,result.origin_z,result.height_origin]):raise ValueError('Non-finite frame origin')
        Transformer.from_crs(result.epsg,4326,always_xy=True)
        if result.world_crs:
            if len(result.world_crs)>1024:raise ValueError('World projection exceeds configuration limit')
            geographic_handoff(result.world_crs,result.epsg)
        return result


def british_grid_tile(east_kilometre,north_kilometre):
    """Return the two-letter/four-digit 1 km OS tile across 100 km boundaries."""
    east_100k=east_kilometre//100;north_100k=north_kilometre//100
    if not 0<=east_100k<=6 or not 0<=north_100k<=12:raise ValueError('Outside British National Grid')
    first=(19-north_100k)-(19-north_100k)%5+(east_100k+10)//5
    second=((19-north_100k)*5)%25+east_100k%5
    if first>7:first+=1
    if second>7:second+=1
    return chr(first+65)+chr(second+65)+f'{east_kilometre%100:02}{north_kilometre%100:02}'


def british_grid_origin(identifier):
    """Decode a two-letter/four-digit grid tile's southwest corner in metres.

    The four digits identify a kilometre corner; England's bulk catalogue uses
    these identifiers for 5 km cells, while Welsh acquisition uses 1 km cells.
    Cell size belongs to the provider, not to this coordinate decoder.
    """
    if not re.fullmatch(r'[A-HJ-Z]{2}[0-9]{4}',identifier):
        raise ValueError('Invalid British grid tile '+str(identifier))
    first,second=[ord(letter)-65-(letter>'I') for letter in identifier[:2]]
    east_100k=((first-2)%5)*5+second%5
    north_100k=19-(first//5)*5-second//5
    east=east_100k*100000+int(identifier[2:4])*1000
    north=north_100k*100000+int(identifier[4:6])*1000
    if british_grid_tile(east//1000,north//1000)!=identifier:
        raise ValueError('Outside British National Grid')
    return east,north
