import unittest
from transport_metadata import national_speed,controls
from shapely.geometry import LineString
import numpy as np
class NationalSpeedIdentity(unittest.TestCase):
 def test_explicit_nsl(self):
  for raw in ('GB:nsl_single','GB:nsl_dual','GB:nsl'):
   self.assertTrue(national_speed({'maxspeed':raw}))
 def test_numeric_without_nsl_is_not_guessed(self):
  for raw in ('60','70','60 mph','70 mph'):
   self.assertFalse(national_speed({'maxspeed':raw}))
 def test_numeric_with_source(self):
  for key in ('source:maxspeed','maxspeed:type'):
   self.assertTrue(national_speed({'maxspeed':'60 mph',key:'GB:nsl_single'}))
   self.assertTrue(national_speed({'maxspeed':'70',key:'GB:nsl_dual'}))
   self.assertFalse(national_speed({'maxspeed':'50',key:'GB:nsl_single'}))
 def test_directional_restrictions_win(self):
  tags={'maxspeed':'70 mph','source:maxspeed':'GB:nsl_dual','maxspeed:backward':'60 mph'}
  self.assertTrue(national_speed(tags,1));self.assertFalse(national_speed(tags,-1))
  tags={'maxspeed':'GB:nsl_single','maxspeed:forward':'60 mph'}
  self.assertFalse(national_speed(tags,1));self.assertTrue(national_speed(tags,-1))
 def test_reversed_way(self):
  tags={'maxspeed':'GB:nsl_single','maxspeed:backward':'50 mph','oneway':'-1'}
  self.assertFalse(national_speed(tags,1));self.assertTrue(national_speed(tags,-1))
 def test_generated_controls_preserve_directional_restriction(self):
  feature=dict(category='road',sourceNodeIds=[],tags={'maxspeed':'70 mph','source:maxspeed':'GB:nsl_dual','maxspeed:backward':'60 mph'},sourceId='fixture',line=LineString([(0,0),(1000,0)]),stations=np.array([0.,1000.]),heights=np.array([0.,0.]),oneway=False,roundabout=False)
  controls({'elements':[]},[feature],None,None)
  values={c['direction']:c['value'] for c in feature['controls'] if c['kind']=='speed'}
  self.assertEqual(values,{1:'national',-1:'60 mph'})
 def test_other_default_is_not_nsl_disc(self):
  for marker in ('GB:nsl_restricted','GB:urban','DE:rural','none'):
   self.assertFalse(national_speed({'maxspeed':'30', 'source:maxspeed':marker}))
if __name__=='__main__':unittest.main()
