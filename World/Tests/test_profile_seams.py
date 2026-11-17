"""Committed stream/path edges remain continuous after the final local fit."""
import unittest
import numpy as np
from area_pipeline import anchor_unfitted_profiles, check_seams

class IndependentProfileSeams(unittest.TestCase):
    def feature(self,category='water'):
        stations=np.arange(-150.,151.,2.)
        return dict(id='stream',category=category,stations=stations,heights=20+stations*.01)
    def test_water_pin_is_exact_between_source_samples_and_fades_locally(self):
        feature=self.feature();original=feature['heights'].copy();station=5.25
        target=np.interp(station,feature['stations'],original)+.0963
        self.assertEqual(anchor_unfitted_profiles([feature],[('stream',station,target)]),1)
        self.assertAlmostEqual(np.interp(station,feature['stations'],feature['heights']),target,places=9)
        self.assertEqual(feature['heights'][0],original[0]);self.assertEqual(feature['heights'][-1],original[-1])
    def test_footpath_pins_are_retained_too(self):
        feature=self.feature('path');anchor_unfitted_profiles([feature],[('stream',0.,22.)])
        self.assertAlmostEqual(np.interp(0,feature['stations'],feature['heights']),22.)
    def test_road_profiles_are_left_to_the_constrained_solver(self):
        feature=self.feature('road');original=feature['heights'].copy()
        self.assertEqual(anchor_unfitted_profiles([feature],[('stream',0.,22.)]),0)
        np.testing.assert_array_equal(feature['heights'],original)
    def test_conflicting_committed_edge_heights_are_rejected(self):
        with self.assertRaisesRegex(ValueError,'Conflicting committed'):
            anchor_unfitted_profiles([self.feature()],[('stream',0.,20.),('stream',0.,21.)])
    def test_strict_seam_rejection_remains_enabled(self):
        def model(x0,x1,height):
            return dict(coreMinX=x0,coreMinZ=0,coreMaxX=x1,coreMaxZ=10,features=[dict(sourceId='waterway',points=[dict(x=-1,z=5,y=height),dict(x=2,z=5,y=height)])])
        with self.assertRaisesRegex(ValueError,'Area seam mismatch'):
            check_seams(model(0,1,20),[model(1,2,20.0963)])

if __name__=='__main__':unittest.main()
