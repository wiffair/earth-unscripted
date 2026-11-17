import unittest
from types import SimpleNamespace
from unittest.mock import patch
from shapely.geometry import box
from shapely.prepared import prep
import sign_metadata
class IdentityProjection:
 def transform(self,x,z):return x,z
class SignContexts(unittest.TestCase):
 def test_crossing_boundary_with_interior_segments(self):
  country=box(0,0,10,10)
  feature={"points":[{"x":2,"z":2},{"x":4,"z":2},{"x":12,"z":2}]}
  with patch.object(sign_metadata,"countries",return_value=[("GB-SCT",country,prep(country))]),patch.object(sign_metadata,"geographic",return_value=IdentityProjection()):
   rows=sign_metadata.contexts(feature,SimpleNamespace(world_projection="test",world_origin=(0,0)))
  self.assertEqual(rows,[dict(start=0.,end=8.,region="GB-SCT"),dict(start=8.,end=10.,region="")])
 def test_outside_uk(self):
  country=box(0,0,10,10)
  feature={"points":[{"x":20,"z":20},{"x":30,"z":20}]}
  with patch.object(sign_metadata,"countries",return_value=[("GB-SCT",country,prep(country))]),patch.object(sign_metadata,"geographic",return_value=IdentityProjection()):
   self.assertEqual(sign_metadata.contexts(feature,SimpleNamespace(world_projection="test",world_origin=(0,0))),[dict(start=0.,end=10.,region="")])
