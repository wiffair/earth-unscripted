"""Shared-node covered-road entrances are not immediate stacked tunnel roofs."""
import unittest
import numpy as np
from shapely.geometry import LineString
from build_map import line_samples
from source_topology import point_identities
from tunnel_profiles import constraints
import profile_safety

class UndergroundRoadEntrances(unittest.TestCase):
    def road(self,identity,xy,nodes,layer):
        original=np.asarray(xy,dtype=float);line=LineString(original);stations,sampled=line_samples(line)
        tags=dict(highway='unclassified' if layer==0 else 'service',tunnel='building_passage' if layer==0 else 'yes')
        if layer:tags['layer']=str(layer)
        return dict(id=identity,sourceId=identity,category='road',kind=tags['highway'],tags=tags,layer=layer,tunnel=True,bridge=False,roundabout=False,width=7. if layer==0 else 4.,tunnelClearance=5. if layer==0 else 1.9,originalXY=original,originalNodeIds=nodes,xy=sampled,stations=stations,heights=np.zeros(len(stations)),raw=np.zeros(len(stations)),line=line,surfaceLine=line,sourceNodeIds=point_identities(identity,original,nodes,sampled))
    def pair(self,shared=True):
        return [self.road('street',[(-12,0),(0,0),(120,0)],[1,2,3],0),self.road('garage',[(0,0),(60,18)],[2 if shared else 20,4],-1)]
    def test_actual_entrance_throat_does_not_require_roof_between_joined_roads(self):
        roads=self.pair();covers=constraints(roads)
        self.assertTrue(all(np.min(c['lowerStations'])>20 for c in covers))
        result=profile_safety.road_grades(roads,[],{},.4)
        self.assertGreater(result['nodes'],0)
        self.assertLessEqual(result['maximumCurvatureRatio'],1.002)
        self.assertAlmostEqual(float(np.interp(12,roads[0]['stations'],roads[0]['heights'])),float(roads[1]['heights'][0]),places=7)
    def test_coincident_coordinates_without_shared_osm_node_retain_clearance(self):
        covers=constraints(self.pair(False));self.assertTrue(covers)
        self.assertTrue(any(c['lower']['id']=='garage' and c['upper']['id']=='street' for c in covers))
    def test_missing_original_node_evidence_cannot_expand_the_throat_exception(self):
        roads=self.pair()
        for road in roads:road.pop('originalNodeIds')
        self.assertTrue(constraints(roads))
    def test_later_crossing_in_connected_loop_keeps_full_roof_clearance(self):
        street=self.pair()[0]
        garage=self.road('garage',[(0,0),(0,80),(80,80),(80,-20)],[2,4,5,6],-1)
        covers=constraints([street,garage]);self.assertTrue(covers)
        self.assertTrue(any(np.any(c['lowerStations']>200) for c in covers))
    def test_unconnected_perpendicular_bores_keep_clearance(self):
        a=self.road('street',[(-80,0),(80,0)],[1,2],0)
        b=self.road('garage',[(0,-80),(0,80)],[3,4],-1)
        self.assertTrue(constraints([a,b]))

if __name__=='__main__':unittest.main()
