"""Adjacent halo clips retain the same source-relative vertical-curve knots."""
import unittest
import numpy as np
from shapely.geometry import LineString, box, Point
from build_map import line_samples, PROFILE_STEP

class ProfileSampling(unittest.TestCase):
    def test_overlapping_clips_share_regular_knots_and_retain_source_vertices(self):
        source=LineString([(0.,0.),(27.3,1.2),(90.,8.),(170.,8.)])
        clips=[source.intersection(box(left,-20,right,30)) for left,right in [(3.7,130.),(22.2,151.)]]
        rows=[line_samples(piece,source) for piece in clips]
        for piece,(stations,xy) in zip(clips,rows):
            np.testing.assert_allclose(xy[0],piece.coords[0]);np.testing.assert_allclose(xy[-1],piece.coords[-1])
            for vertex in piece.coords:self.assertLess(np.min(np.linalg.norm(xy-np.array(vertex),axis=1)),1e-7)
            self.assertLessEqual(np.max(np.diff(stations)),PROFILE_STEP+1e-7)
        shared=[]
        for stations,xy in rows:
            shared.append({round(source.project(Point(point)),6) for point in xy if 30.01<source.project(Point(point))<119.99})
        self.assertEqual(shared[0],shared[1]);self.assertGreater(len(shared[0]),30)
    def test_reversed_source_way_keeps_shared_knots(self):
        source=LineString([(150.,4.),(80.,4.),(0.,0.)])
        samples=[]
        for left,right in [(1.5,130.),(19.3,149.)]:
            piece=source.intersection(box(left,-10,right,10));_,xy=line_samples(piece,source)
            samples.append({round(source.project(Point(p)),6) for p in xy if 30.01<source.project(Point(p))<109.99})
        self.assertEqual(samples[0],samples[1])
    def test_legacy_uncropped_call_retains_bounded_samples(self):
        line=LineString([(0.,0.),(3.5,0.),(10.,0.)]);stations,xy=line_samples(line)
        np.testing.assert_allclose(stations,[0,2,3.5,4,6,8,10])

if __name__=='__main__':unittest.main()
