import unittest
from build_map import surface_audio_metadata
class SurfaceAudio(unittest.TestCase):
 def test_material_and_condition(self):
  self.assertEqual(surface_audio_metadata({'surface':'Concrete:Plates','smoothness':' very_bad '}),{'surface':'concrete:plates','smoothness':'very_bad'})
 def test_absent_is_not_guessed_age(self):
  self.assertEqual(surface_audio_metadata({}),{'surface':'','smoothness':''})
 def test_bounded_and_typed(self):
  self.assertEqual(surface_audio_metadata({'surface':None,'smoothness':42}),{'surface':'','smoothness':''})
  self.assertEqual(len(surface_audio_metadata({'surface':'x'*1000})['surface']),64)
