"""Distant relief must survive valid deep-water cells in coastal source tiles."""
from pathlib import Path
import tempfile
import unittest
import numpy as np
from PIL import Image
from far_terrain import FarTerrain, ZOOM

class CoastalElevationTiles(unittest.TestCase):
    def setUp(self):
        self.directory=tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.terrain=FarTerrain(Path(self.directory.name),lambda *args,**kwargs:None)
    def tile(self,heights):
        encoded=np.round((np.asarray(heights)+32768)*256).astype(np.int64)
        rgb=np.stack(((encoded>>16)&255,(encoded>>8)&255,encoded&255),axis=-1).astype(np.uint8)
        Image.fromarray(rgb).save(self.terrain.raw/f'{ZOOM}_0_0.png')
        return self.terrain.tile((0,0))[1]
    def test_coastal_bathymetry_does_not_discard_visible_mountains(self):
        heights=np.full((256,256),1327.25)
        heights[0,0]=-919
        decoded=self.tile(heights)
        self.assertEqual(decoded[0,0],-919)
        self.assertEqual(decoded[128,128],1327.25)
    def test_deep_ocean_is_valid_earth_elevation(self):
        self.assertTrue(np.all(self.tile(np.full((256,256),-10984))==-10984))
    def test_nodata_sentinel_is_rejected(self):
        with self.assertRaisesRegex(ValueError,'Invalid coarse terrain'):
            self.tile(np.full((256,256),-32768))
    def test_corrupt_impossible_elevation_is_rejected(self):
        with self.assertRaisesRegex(ValueError,'Invalid coarse terrain'):
            self.tile(np.full((256,256),15000))

if __name__=='__main__':unittest.main()
