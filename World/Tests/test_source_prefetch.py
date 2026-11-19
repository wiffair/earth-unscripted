import unittest
from live_routing import Forecast,source_forecast_metres,GEOMETRY_FORECAST_METRES
from area_server import retained_forecast_model
class SourcePrefetch(unittest.TestCase):
    def models(self):
        def model(tile,start,end):
            points=[dict(x=float(x),y=0.,z=0.) for x in range(start,end+1,500)]
            feature=dict(category='road',kind='primary',ref='A1',oneway=True,roundabout=False,points=points,sourceNodeIds=['n'+str(int(p['x'])) for p in points])
            return dict(tileX=tile,tileZ=0,features=[feature],heights=[0.]*100,buildings=[dict(example=True)])
        return [model(0,0,2000),retained_forecast_model(model(1,1500,4000),True,'hash')]
    def request(self,**extra):return dict(x=0.,z=0.,start=dict(x=0.,y=0.,z=0.),headingX=1.,headingZ=0.,loaded=[dict(x=0,z=0)],**extra)
    def test_warm_graph_extends_known_route_beyond_client_resident_area(self):
        result=Forecast().reachable(self.models(),self.request(sourceLookAheadMetres=6000))
        self.assertTrue(any(r['tileX']==2 for r in result['candidates']))
        self.assertTrue(any(r['tileX']==1 for r in result['candidates'])) # warm core remains available for near-car promotion
        self.assertTrue(all(r['distanceMetres']<=GEOMETRY_FORECAST_METRES for r in result['corridors']))
    def test_distant_cache_does_not_expand_default_geometry_window(self):
        result=Forecast().reachable(self.models(),self.request())
        self.assertTrue(all(r['distanceMetres']<=1800 for r in result['candidates']))
    def test_warm_models_retain_roads_not_terrain_or_building_arrays(self):
        warm=self.models()[1];self.assertNotIn('heights',warm);self.assertNotIn('buildings',warm);self.assertEqual(warm['_forecastRevision'],'hash');self.assertTrue(all(f['category']=='road' for f in warm['features']))
    def test_integer_cardinal_heading_from_json_is_supported(self):
        request=self.request(sourceLookAheadMetres=6000);request.update(headingX=0,headingZ=-1)
        result=Forecast().reachable(self.models(),request)
        self.assertTrue(result['candidates'])
    def test_planning_horizon_is_bounded(self):
        for value,expected in [(None,1800),(-1,1800),(float('nan'),1800),(float('inf'),1800),(10000,7680),(5000,5000)]:
            with self.subTest(value=value):self.assertEqual(source_forecast_metres(dict(sourceLookAheadMetres=value)),expected)
if __name__=='__main__':unittest.main()
