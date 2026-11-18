import unittest
import numpy as np
from shapely.geometry import LineString,Polygon,Point
import railway_furniture,transport_metadata
from build_map import osm_layer
class Identity:
 def transform(self,x,z):return x,z
def way(i,nodes,points,tags):return dict(type='way',id=i,nodes=nodes,geometry=[dict(lon=x,lat=z) for x,z in points],tags=tags)
def rail(row,height=0):
 points=np.array([(p['lon'],p['lat']) for p in row['geometry']],float);line=LineString(points)
 return dict(id=str(row['id']),sourceId=str(row['id']),category='rail',layer=osm_layer(row['tags']),tags=row['tags'],sourceNodeIds=['osm:'+str(n) for n in row['nodes']],line=line,stations=np.linspace(0,line.length,len(points)),heights=np.full(len(points),height),width=3.8,**transport_metadata.railway(row['tags'],'GB'))
def triangle_area(points):return sum(Polygon([(p['x'],p['z']) for p in points[i:i+3]]).area for i in range(0,len(points),3))
class RailwayFurniture(unittest.TestCase):
 def prepare(self,rows,features=None):return railway_furniture.derive(dict(elements=rows),features if features is not None else [rail(r) for r in rows if r.get('tags',{}).get('railway') in railway_furniture.TRACKS and r['type']=='way'],Identity(),np.zeros(2),(-60,-60,60,60),'GB',osm_layer)
 def test_real_endpoints_not_way_split_or_junction(self):
  a=way(1,[1,2],[(-40,0),(0,0)],dict(railway='rail'));b=way(2,[2,3],[(0,0),(40,0)],dict(railway='rail'))
  buffers,_,_=self.prepare([a,b]);self.assertEqual([b['id'] for b in buffers],['buffer:1','buffer:3'])
  self.assertTrue(all(abs(b['x'])==40 for b in buffers));self.assertTrue(all(b['dx']*b['x']<0 for b in buffers))
 def test_inactive_tracks_never_get_inferred_buffers_but_mapped_stop_remains(self):
  for key in ['disused','abandoned','dismantled','demolished','proposed','construction']:
   with self.subTest(status=key):
    row=way(1,[1,2],[(-40,0),(40,0)],dict(railway='rail',**{key:'yes'}))
    self.assertEqual(self.prepare([row])[0],[])
    stop=dict(type='node',id=1,lon=-40,lat=0,tags=dict(railway='buffer_stop'))
    self.assertEqual([s['id'] for s in self.prepare([row,stop])[0]],['buffer:1'])
 def test_mapped_platform_height_overrides_country_estimate(self):
  row=way(1,[1,2],[(-40,0),(40,0)],dict(railway='rail'));p=self.platform(False);p['tags']['height']='0.55'
  _,platforms,_=self.prepare([row,p]);self.assertTrue(all(abs(v['y']-.55)<1e-6 for v in platforms[0]['triangles']));self.assertEqual(platforms[0]['heightSource'],'mapped OSM height')
 def test_chunk_or_query_clipped_ends_are_not_terminal(self):
  row=way(1,[1,2],[(-100,0),(100,0)],dict(railway='rail'));f=rail(row);f.update(line=LineString([(-60,0),(60,0)]),stations=np.array([0.,120.]),sourceNodeIds=['clip:left','clip:right'])
  self.assertEqual(self.prepare([row],[f])[0],[])
 def test_no_buffer_for_tram_turnback_without_mapped_stop(self):
  row=way(1,[1,2],[(-40,0),(40,0)],dict(railway='tram'));self.assertEqual(self.prepare([row])[0],[])
 def test_explicit_stop_has_priority_at_query_edge(self):
  row=way(1,[1,2],[(-55,0),(40,0)],dict(railway='rail'));stop=dict(type='node',id=1,lon=-55,lat=0,tags=dict(railway='buffer_stop'))
  buffers,_,_=self.prepare([row,stop]);self.assertEqual(len(buffers),2);self.assertTrue(next(b for b in buffers if b['id']=='buffer:1')['mapped'])
 def platform(self,covered=True):return way(9,[91,92,93,94,91],[(0,2),(30,2),(30,6),(0,6),(0,2)],dict(railway='platform',covered='yes' if covered else 'no',ref='2'))
 def test_platform_uses_rail_height_station_name_and_wire_clearance(self):
  r=way(1,[1,2],[(-40,0),(40,0)],dict(railway='rail',electrified='contact_line'));station=dict(type='node',id=101,lon=10,lat=8,tags=dict(railway='station',name='Source Station'))
  _,platforms,_=self.prepare([r,self.platform(),station]);self.assertEqual(len(platforms),1);p=platforms[0]
  self.assertEqual(p['name'],'Source Station · 2');self.assertAlmostEqual(triangle_area(p['triangles']),120);self.assertTrue(all(abs(v['y']-.915)<1e-6 for v in p['triangles']))
  self.assertTrue(all(v['y']>=transport_metadata.railway(r['tags'],'GB')['railClearanceHeight']+.249 for v in p['roofTriangles']))
  self.assertTrue(p['posts']);self.assertTrue(all(abs(v['z'])>=2.3 for v in p['posts']));self.assertGreater(p['depth'],.915)
 def test_platform_does_not_fill_road_or_train_body(self):
  r=way(1,[1,2],[(-40,0),(40,0)],dict(railway='rail'));road=dict(id='road',category='road',line=LineString([(15,-20),(15,20)]),stations=np.array([0.,40.]),heights=np.zeros(2),width=6)
  _,platforms,_=self.prepare([r,self.platform()],[rail(r),road]);self.assertEqual(len(platforms),2)
  for p in platforms:
   for i in range(0,len(p['triangles']),3):
    triangle=Polygon([(v['x'],v['z']) for v in p['triangles'][i:i+3]]);self.assertLess(triangle.intersection(road['line'].buffer(3.249)).area,1e-7)
 def test_roof_source_not_duplicated_and_mapped_height_retained(self):
  r=way(1,[1,2],[(-40,0),(40,0)],dict(railway='rail'));roof=self.platform();roof.update(id=10,tags=dict(building='roof',min_height='7'))
  _,platforms,consumed=self.prepare([r,self.platform(),roof]);self.assertEqual(consumed,{'10'});self.assertAlmostEqual(triangle_area(platforms[0]['roofTriangles']),120);self.assertTrue(all(v['y']>=7 for v in platforms[0]['roofTriangles']))
 def test_bus_only_platform_does_not_become_train_platform(self):
  r=way(1,[1,2],[(-40,0),(40,0)],dict(railway='rail'));p=self.platform();p['tags']=dict(public_transport='platform',bus='yes')
  self.assertEqual(self.prepare([r,p])[1],[])
 def test_platform_multipolygon_courtyard_is_not_filled(self):
  r=way(1,[1,2],[(-40,0),(40,0)],dict(railway='rail'));p=self.platform(False);p['_holes']=[[dict(lon=x,lat=z) for x,z in [(10,3),(20,3),(20,5),(10,5),(10,3)]]]
  _,platforms,_=self.prepare([r,p]);self.assertAlmostEqual(triangle_area(platforms[0]['triangles']),100)
 def test_explicit_platform_layer_avoids_nearest_upper_track(self):
  lower=way(1,[1,2],[(-40,0),(40,0)],dict(railway='rail',layer='0'));upper=way(2,[3,4],[(-40,3),(40,3)],dict(railway='rail',bridge='yes',layer='1'));p=self.platform(False);p['tags']['layer']='0'
  _,platforms,_=self.prepare([lower,upper,p],[rail(lower),rail(upper,8)])
  self.assertTrue(platforms);self.assertTrue(all(abs(v['y']-.915)<1e-6 for v in platforms[0]['triangles']))
if __name__=='__main__':unittest.main()
