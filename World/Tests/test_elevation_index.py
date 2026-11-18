"""Differential coverage tests against the original ordered full raster scan."""
import math
import unittest
from unittest.mock import patch
import numpy as np
from rasterio.coords import BoundingBox
from scipy.ndimage import map_coordinates
import build_map


def original_sample(elevation, xy, kind='dtm'):
    xy=np.asarray(xy)+build_map.ORIGIN
    if elevation.to_source is not None:
        east,north=elevation.to_source.transform(xy[:,0],xy[:,1]);xy=np.c_[east,north]
    result=np.full(len(xy),np.nan);trusted=np.zeros(len(xy),dtype=bool)
    for bounds,data,resolution,valid in elevation.rasters[kind]:
        mask=(xy[:,0]>=bounds.left)&(xy[:,0]<bounds.right)&(xy[:,1]>=bounds.bottom)&(xy[:,1]<bounds.top)
        if not mask.any():continue
        coordinates=[(bounds.top-xy[mask,1])/resolution[1]-.5,(xy[mask,0]-bounds.left)/resolution[0]-.5]
        sampled=map_coordinates(data,coordinates,order=1,mode='nearest')
        observed=map_coordinates(valid,coordinates,order=0,mode='nearest')>0 if valid is not None else np.ones(len(sampled),dtype=bool)
        indices=np.flatnonzero(mask);accepted=observed|~trusted[indices]
        result[indices[accepted]]=sampled[accepted];trusted[indices[observed]]=True
    if not np.all(np.isfinite(result)):raise ValueError('Missing elevation coverage')
    return result-build_map.HEIGHT_ORIGIN


def original_measured(elevation,xy,kind='dtm'):
    xy=np.asarray(xy)+build_map.ORIGIN
    if elevation.to_source is not None:
        east,north=elevation.to_source.transform(xy[:,0],xy[:,1]);xy=np.c_[east,north]
    observed=np.zeros(len(xy),dtype=bool)
    for bounds,resolution,valid in elevation.survey_rasters[kind]:
        mask=(xy[:,0]>=bounds.left)&(xy[:,0]<bounds.right)&(xy[:,1]>=bounds.bottom)&(xy[:,1]<bounds.top)
        if not mask.any():continue
        indices=np.flatnonzero(mask)
        if valid is None:observed[indices]=True
        else:
            coordinates=[(bounds.top-xy[mask,1])/resolution[1]-.5,(xy[mask,0]-bounds.left)/resolution[0]-.5]
            observed[indices[map_coordinates(valid,coordinates,order=0,mode='nearest')>0]]=True
    return observed


class ElevationIndexTests(unittest.TestCase):
    def setUp(self):
        self.origin=patch.object(build_map,'ORIGIN',np.array([0.,0.]));self.origin.start();self.addCleanup(self.origin.stop)
        self.height=patch.object(build_map,'HEIGHT_ORIGIN',9.);self.height.start();self.addCleanup(self.height.stop)
        rng=np.random.default_rng(812);rows=[]
        for x in range(-8,9):
            for z in range(-6,7):
                bounds=BoundingBox(x*900.,z*900.,x*900.+1050,z*900.+1050)
                data=rng.normal(100,10,(7,7));valid=None if (x+z)%3 else rng.integers(0,2,(7,7),dtype=np.uint8)
                rows.append((bounds,data,(150.,150.),valid))
        # Overlapping measured/filled surfaces must retain original precedence.
        rows.append((BoundingBox(-500,-500,500,500),np.full((4,4),500.),(250.,250.),None))
        rows.append((BoundingBox(-500,-500,500,500),np.full((4,4),900.),(250.,250.),np.zeros((4,4),dtype=np.uint8)))
        e=self.elevation=build_map.Elevation.__new__(build_map.Elevation);e.to_source=None;e.progress_samples=0
        e.rasters={k:rows for k in ('dtm','dsm')};e.survey_rasters={k:[(r[0],r[2],r[3]) for r in rows[::2]] for k in e.rasters}
        e.raster_indexes={k:build_map.RasterCoverageIndex(v) for k,v in e.rasters.items()};e.survey_indexes={k:build_map.RasterCoverageIndex(v) for k,v in e.survey_rasters.items()}

    def compare(self,xy):
        for kind in ('dtm','dsm'):
            try:expected=original_sample(self.elevation,xy,kind)
            except ValueError:
                with self.assertRaisesRegex(ValueError,'Missing elevation coverage'):self.elevation.sample(xy,kind)
            else:np.testing.assert_array_equal(self.elevation.sample(xy,kind),expected)
            np.testing.assert_array_equal(self.elevation.measured_at(xy,kind),original_measured(self.elevation,xy,kind))

    def test_batch_and_single_points_exact(self):
        rng=np.random.default_rng(44);xy=rng.uniform([-7100,-5300],[7900,6100],(1500,2));self.compare(xy)
        for row in xy[::7]:self.compare(row[None,:])

    def test_edges_negative_cells_missing_and_nonfinite(self):
        for x in [-7200.,-1024.,-500.,0.,500.,1024.,8250.]:
            for z in [-5400.,-1024.,0.,500.,1024.,6450.]:
                for delta in [-1e-8,0,1e-8]:self.compare(np.array([[x+delta,z+delta]]))
        for xy in [np.empty((0,2)),np.array([[math.nan,0.]]),np.array([[math.inf,0.]]),np.array([[0.,0.],[1e9,1e9]])]:self.compare(xy)

    def test_transformed_coordinates(self):
        class Transform:
            def transform(self,x,z):return x+101.25,z-731.5
        self.elevation.to_source=Transform();self.compare(np.array([[0.,0.],[-1024.,1024.],[500.,500.]]))

    def test_large_raster_fallback_and_sparse_filter(self):
        rows=[(BoundingBox(-1e8,-1e8,1e8,1e8),),(BoundingBox(0,0,10,10),)]
        index=build_map.RasterCoverageIndex(rows)
        self.assertEqual(list(index.candidates(np.array([[5.,5.]]))),[0,1])
        self.assertEqual(list(index.candidates(np.array([[1e6,1e6]]))),[0])
        self.assertLess(len(self.elevation.raster_indexes['dtm'].candidates(np.array([[25.,25.]]))),len(self.elevation.rasters['dtm'])//10)

if __name__=='__main__':unittest.main()
