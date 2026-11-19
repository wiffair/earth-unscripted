import unittest
import numpy as np
from shapely.geometry import LineString,Point
from tunnel_profiles import constraint_stations
from source_topology import UndergroundRailJunctions
class TunnelConstraints(unittest.TestCase):
 def test_reduced_linear_extrema_match_every_original_sample(self):
  rng=np.random.default_rng(927)
  lower=np.r_[rng.uniform(0,20,500),0.,10.,20.];upper=np.r_[rng.uniform(0,20,500),0.,10.,20.]
  stations=np.array([0.,10.,20.]);a=dict(stations=stations);b=dict(stations=stations)
  cover=dict(lowerStations=lower,upperStations=upper,lower=a,upper=b)
  u,l=constraint_stations(cover);self.assertLess(len(u),len(upper)//2)
  for _ in range(300):
   ah=rng.normal(0,50,3);bh=rng.normal(0,50,3)
   whole=np.interp(upper,stations,ah)-np.interp(lower,stations,bh)
   reduced=np.interp(u,stations,ah)-np.interp(l,stations,bh)
   self.assertAlmostEqual(float(whole.min()),float(reduced.min()),places=10)
 def rail(self,identity,points,nodes,level):
  xy=np.array(points,dtype=float)
  return dict(id=identity,category='rail',tunnel=True,kind='subway',tags=dict(level=level,gauge='1435'),layer=int(level),originalXY=xy,originalNodeIds=nodes,line=LineString(xy))
 def test_actual_local_join_survives_a_source_floor_transition(self):
  a=self.rail('a',[(0,0),(20,0)],[1,2],'-1');b=self.rail('b',[(20,0),(40,0)],[2,3],'-2')
  junctions=UndergroundRailJunctions([a,b]);self.assertTrue(junctions.connected_at(a,b,Point(20,0)));self.assertFalse(junctions.connected_at(a,b,Point(1000,0)))
 def test_adjacent_ballast_at_staggered_floor_boundaries_is_not_a_roof(self):
  from tunnel_profiles import constraints
  features=[self.rail('a',[(-20,0),(0,0)],[1,2],'-2'),self.rail('b',[(0,0),(20,0)],[2,3],'-1'),self.rail('c',[(-16,3.8),(4,3.8)],[4,5],'-2'),self.rail('d',[(4,3.8),(24,3.8)],[5,6],'-1')]
  for f in features:f.update(width=4.2,bridge=False,stations=np.array([0.,20.]))
  self.assertEqual(constraints(features),[])
 def test_unconnected_crossing_does_not_lose_clearance(self):
  a=self.rail('a',[(-20,0),(20,0)],[1,2],'-1');b=self.rail('b',[(0,-20),(0,20)],[3,4],'-2')
  self.assertFalse(UndergroundRailJunctions([a,b]).connected_at(a,b,Point(0,0)))
class AdjustableCrossingPaths(unittest.TestCase):
 def test_path_can_clear_a_pinned_road_without_changing_the_road(self):
  import profile_safety
  def feature(identity,category,xy):
   xy=np.array(xy,dtype=float);stations=np.array([0.,50.,100.]);line=LineString(xy)
   return dict(id=identity,sourceId=identity,category=category,kind='footway' if category=='path' else 'primary',roundabout=False,width=2.5 if category=='path' else 7.,layer=1 if category=='path' else 0,tags={},stations=stations,xy=xy,originalXY=xy,line=line,surfaceLine=line,raw=np.zeros(3),heights=np.zeros(3),tunnel=False,bridge=category=='path',sourceNodeIds=[identity+str(i) for i in range(3)])
  road=feature('road','road',[(0,0),(50,0),(100,0)]);path=feature('path','path',[(50,-50),(50,0),(50,50)])
  crossing=dict(upper='path',lower='road',required=5.,upperStation=50.,lowerStation=50.)
  result=profile_safety.road_grades([road,path],[crossing],{('path','road'):([50.],[50.])},.4,[('road',s,0.) for s in road['stations']])
  self.assertEqual(result['constrainedPaths'],1);self.assertGreaterEqual(path['heights'][1]-road['heights'][1],5.44-1e-7);self.assertLess(np.max(abs(road['heights'])),.0011)
if __name__=='__main__':unittest.main()
