"""Cached proposals may rebuild; delivered profiles and physical limits may not."""
import copy
import unittest
import numpy as np
from shapely.geometry import LineString
from area_pipeline import fit_neighbour_profiles, railway_context_anchors, check_seams
from profile_safety import road_grades, ProfileFitInfeasible

class ProfileCacheReconciliation(unittest.TestCase):
    def fixture(self):
        xy=np.array([[-100.,0.],[0.,0.],[100.,0.],[200.,0.]])
        line=LineString(xy)
        feature=dict(id='track',sourceId='track',category='rail',kind='rail',roundabout=False,width=4.2,layer=0,tags={},stations=np.array([0.,100.,200.,300.]),xy=xy,originalXY=xy,line=line,surfaceLine=line,raw=np.zeros(4),heights=np.zeros(4),tunnel=False,bridge=False,sourceNodeIds=['a','b','c','d'])
        def old(tile,x0,x1,height):
            return dict(tileX=tile,tileZ=0,coreMinX=x0,coreMaxX=x1,coreMinZ=-10.,coreMaxZ=10.,features=[dict(sourceId='track',category='rail',points=[dict(x=float(x),z=0.,y=height) for x in (-100,0,100,200)])])
        model=dict(coreMinX=0.,coreMaxX=100.,coreMinZ=-10.,coreMaxZ=10.)
        return [feature],[old(-1,-100.,0.,0.),old(1,100.,200.,20.)],model
    def solve(self,features,neighbours,model,protected):
        return fit_neighbour_profiles(features,neighbours,model,lambda anchors:road_grades(features,[],{},.75,anchors),protected)
    def test_unused_conflicting_proposal_can_be_rebuilt_without_moving_delivered_seam(self):
        features,neighbours,model=self.fixture();before=copy.deepcopy(neighbours)
        fit,anchors,kept,proposals=self.solve(features,neighbours,model,{(-1,0)})
        self.assertEqual([n['tileX'] for n in kept],[-1]);self.assertEqual([n['tileX'] for n in proposals],[1]);self.assertEqual(neighbours,before)
        self.assertLessEqual(fit['maximumCurvatureRatio'],1.002)
        for identity,station,height in anchors:self.assertLessEqual(abs(np.interp(station,features[0]['stations'],features[0]['heights'])-height),.00101)
        self.assertLessEqual(np.max(abs(np.diff(features[0]['heights'])/100.)),.04000001)
    def test_two_delivered_conflicting_boundaries_still_refuse(self):
        with self.assertRaises(ProfileFitInfeasible):self.solve(*self.fixture(),{(-1,0),(1,0)})
    def test_unknown_ownership_is_conservative(self):
        with self.assertRaises(ProfileFitInfeasible):self.solve(*self.fixture(),None)
    def test_timeout_or_other_solver_failure_is_not_reclassified_as_cache_conflict(self):
        features,neighbours,model=self.fixture()
        def fail(anchors):raise ValueError('time limit reached')
        with self.assertRaisesRegex(ValueError,'time limit'):
            fit_neighbour_profiles(features,neighbours,model,fail,{(-1,0)})
    def test_rail_planning_uses_owned_core_not_old_halo(self):
        features,neighbours,model=self.fixture()
        anchors=railway_context_anchors(features,neighbours[:1],model)
        self.assertTrue(anchors)
        for identity,station,height in anchors:
            x=float(np.interp(station,features[0]['stations'],features[0]['xy'][:,0]))
            self.assertGreaterEqual(x,-100.);self.assertLessEqual(x,0.)
    def test_synthetic_regenerated_edge_passes_independent_seam_census(self):
        features,neighbours,model=self.fixture();fit,anchors,kept,proposals=self.solve(features,neighbours,model,{(-1,0)})
        model['features']=[dict(sourceId='track',points=[dict(x=float(p[0]),z=float(p[1]),y=float(y)) for p,y in zip(features[0]['xy'],features[0]['heights'])])]
        self.assertLessEqual(check_seams(model,kept)['maximumHeightGapMetres'],.025)
        with self.assertRaisesRegex(ValueError,'seam mismatch'):check_seams(model,proposals)

    def test_diagonal_owned_core_contributes_planning_without_shared_edge(self):
        features,neighbours,model=self.fixture();feature=features[0]
        xy=np.array([[-100.,-30.],[-50.,-20.],[10.,10.],[100.,100.]])
        feature.update(xy=xy,line=LineString(xy),stations=np.r_[0.,np.cumsum(np.linalg.norm(np.diff(xy,axis=0),axis=1))])
        old=dict(tileX=-1,tileZ=-1,coreMinX=-100.,coreMaxX=0.,coreMinZ=-100.,coreMaxZ=0.,features=[dict(sourceId='track',category='rail',points=[dict(x=float(p[0]),z=float(p[1]),y=-10.) for p in xy])])
        model.update(coreMinX=0.,coreMaxX=100.,coreMinZ=0.,coreMaxZ=100.)
        from area_pipeline import published_road_anchors
        self.assertEqual(published_road_anchors(features,[old],model),[])
        self.assertTrue(railway_context_anchors(features,[old],model))
    def cached_proposal(self,height):
        import tempfile,json,time,hashlib
        from pathlib import Path
        from area_pipeline import Pipeline
        features,neighbours,model=self.fixture()
        model['features']=[dict(sourceId='track',category='rail',points=[dict(x=float(p[0]),z=float(p[1]),y=height) for p in features[0]['xy']])]
        temp=tempfile.TemporaryDirectory();self.addCleanup(temp.cleanup)
        pipeline=Pipeline.__new__(Pipeline);pipeline.prepared=Path(temp.name);pipeline.compatible={'fixture'};pipeline.protected_tiles={(-1,0)};pipeline._neighbour_models=lambda bounds,allowed:neighbours[:1]
        events=[]
        def event(kind,**fields):
            events.append(kind)
            if fields.get('stage')=='Fetching map and elevation sources':raise RuntimeError('rebuild reached')
        pipeline.event=event
        output=pipeline.prepared/'0_0.json';output.write_text(json.dumps(model));manifest=dict(generator='fixture',sha256=hashlib.sha256(output.read_bytes()).hexdigest(),createdUTC=time.time())
        (pipeline.prepared/'0_0.manifest.json').write_text(json.dumps(manifest))
        return pipeline,events
    def test_incompatible_cached_proposal_is_rebuilt_before_delivery(self):
        pipeline,events=self.cached_proposal(20.)
        with self.assertRaisesRegex(RuntimeError,'rebuild reached'):pipeline._prepare_locked((0,0))
        self.assertIn('prepared-cache-seam-rebuild',events);self.assertNotIn('prepared-cache-hit',events)
    def test_compatible_cached_area_is_reused(self):
        pipeline,events=self.cached_proposal(0.)
        model,manifest,cached=pipeline._prepare_locked((0,0))
        self.assertTrue(cached);self.assertIn('prepared-cache-hit',events)

if __name__=='__main__':unittest.main()
