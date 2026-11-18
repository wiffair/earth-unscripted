"""Real derive() regressions for ambiguous/malformed worldwide OSM layer tags."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import numpy as np
import build_map

class Elevation:
    def sample(self,xy,kind='dtm'):return np.full(len(xy),12. if kind=='dsm' else 0.)
    def measured_at(self,xy,kind='dtm'):return np.zeros(len(xy),dtype=bool)
class Identity:
    def transform(self,x,z):return x,z
class HamburgLocal:
    def transform(self,x,z):return (x-10.0164)*100000,(z-53.55073)*100000

def way(identifier,points,tags):
    return dict(type='way',id=identifier,nodes=[identifier*100+i for i in range(len(points))],geometry=[dict(lon=x,lat=z) for x,z in points],tags=tags)

class OsmLayers(unittest.TestCase):
    def derive(self,elements,projection=None):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);(root/'Sources').mkdir();(root/'Sources/osm-area-complete.json').write_text(json.dumps(dict(elements=elements)))
            with patch.multiple(build_map,ROOT=root,ORIGIN=np.array([0.,0.]),BOUNDS=(-80.,-80.,80.,80.),GRID_STEP=8.,COUNTRY_CODE='DE',ROUTE_PLAN=dict(name='Parser fixture',edges=[])),patch.object(build_map,'Elevation',Elevation),patch.object(build_map.Transformer,'from_crs',return_value=projection or Identity()),patch.object(build_map.canopy,'derive',return_value=([],{})):
                return build_map.derive()
    def test_explicit_single_layers_preserved(self):
        for raw,value in [('0',0),('  -2 ',-2),('+3',3),('9',9),('1.0',1),('2147483647',2147483647),('-2147483648',-2147483648)]:
            for context in ({},{'bridge':'yes'},{'tunnel':'yes'}):
                with self.subTest(raw=raw,context=context):self.assertEqual(build_map.osm_layer(dict(context,layer=raw)),value)
    def test_semicolon_context_and_invalid_tokens(self):
        for raw in ['0;-1','-1;0','garbage;0;-1;','0 ; -1']:
            self.assertEqual(build_map.osm_layer(dict(layer=raw,tunnel='building_passage')),-1)
        for raw in ['1;2','2;1','oops;1;2']:
            self.assertEqual(build_map.osm_layer(dict(layer=raw,bridge='yes')),2)
        self.assertEqual(build_map.osm_layer(dict(layer='0;-1')),0)
        self.assertEqual(build_map.osm_layer(dict(layer='-1;0')),-1)
        self.assertEqual(build_map.osm_layer(dict(layer='0;-1',location='underground')),-1)
    def test_missing_and_bad_values_bounded(self):
        self.assertEqual(build_map.osm_layer({'bridge':'yes'}),0)
        for raw in ['no','nan','Infinity','1.7','2147483648',[],{},'9'*10000]:
            self.assertEqual(build_map.osm_layer(dict(layer=raw,bridge='yes')),1)
            self.assertEqual(build_map.osm_layer(dict(layer=raw,tunnel='yes')),-1)
            self.assertEqual(build_map.osm_layer(dict(layer=raw)),0)
    def test_actual_hamburg_passage_completes_numeric_derivation(self):
        data=json.loads((Path(__file__).parent/'Fixtures/Layers/hamburg-passage.json').read_text())
        model,report,features=self.derive(data['elements'],HamburgLocal())
        self.assertEqual(len(features),1);self.assertEqual(features[0]['id'],'1351409223')
        self.assertEqual(features[0]['layer'],-1);self.assertTrue(features[0]['tunnel'])
        self.assertEqual(model['features'][0]['layer'],-1)
        self.assertTrue(np.isfinite([p['y'] for p in model['features'][0]['points']]).all())
        self.assertEqual(data['elements'][0]['tags']['layer'],'0;-1')
    def test_building_site_uses_same_parser_without_burying_ordinary_buildings(self):
        elements=[way(10,[(-40,0),(40,0)],dict(highway='residential')),
          way(20,[(10,20),(20,20),(20,30),(10,30),(10,20)],dict(building='yes',layer='0;-1')),
          way(21,[(-30,20),(-20,20),(-20,30),(-30,30),(-30,20)],dict(building='yes',layer='0;-1',location='underground'))]
        model,_,_=self.derive(elements)
        self.assertEqual([b['layer'] for b in model['buildings']],[0,-1]);self.assertEqual([b['underground'] for b in model['buildings']],[False,True])
    def test_stacked_bridge_clearance_keeps_layer_order_and_physical_gap(self):
        model,report,features=self.derive([
          way(30,[(-40,0),(40,0)],dict(highway='primary',bridge='yes',layer='1;2')),
          way(31,[(0,-40),(0,40)],dict(railway='rail',bridge='yes',layer='0;1'))])
        self.assertEqual([f['layer'] for f in features],[2,1]);self.assertGreater(report['stackedBridgeCrossings'],0)
        self.assertTrue(report['clearances']);self.assertTrue(all(c['upper']=='30' and c['lower']=='31' and c['passes'] for c in report['clearances']))
        self.assertTrue(np.isfinite(model['heights']).all())

if __name__=='__main__':unittest.main()
