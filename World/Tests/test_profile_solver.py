import tempfile,time,unittest,subprocess,sys
from pathlib import Path
from unittest.mock import patch
import numpy as np
from scipy.sparse import csr_matrix
import profile_solver
class NumericSolver(unittest.TestCase):
    def tearDown(self):profile_solver.configure()
    def test_cancellable_process_returns_same_numeric_solution(self):
        profile_solver.configure(lambda:None)
        result=profile_solver.solve(np.array([-1.]),A_ub=csr_matrix([[1.]]),b_ub=np.array([3.]),bounds=[(0,None)],method='highs',options={'time_limit':10})
        self.assertTrue(result.success);self.assertAlmostEqual(result.x[0],3.)
    def test_cancellation_terminates_own_child_promptly(self):
        class Cancelled(BaseException):pass
        count=0;children=[];original=subprocess.Popen
        def checkpoint():
            nonlocal count
            count+=1
            if count>=3:raise Cancelled()
        def sleeper(command,**options):
            child=original([sys.executable,'-c','import time;time.sleep(30)'],**options);children.append(child);return child
        profile_solver.configure(checkpoint);started=time.monotonic()
        with patch.object(profile_solver.subprocess,'Popen',sleeper),self.assertRaises(Cancelled):
            profile_solver.solve(np.array([-1.]),A_ub=csr_matrix([[1.]]),b_ub=np.array([3.]),bounds=[(0,None)])
        self.assertLess(time.monotonic()-started,3);self.assertEqual(len(children),1);self.assertIsNotNone(children[0].poll())
    def test_numeric_child_stops_if_parent_pipe_closes(self):
        import json,os
        with tempfile.TemporaryDirectory(prefix='EarthUnscriptedProfileSolve-') as name:
            root=Path(name);np.savez(root/'input.npz',c=np.array([-1.]),data=np.array([1.]),indices=np.array([0]),indptr=np.array([0,1]),shape=np.array([1,1]),b=np.array([3.]),bounds=np.array([[0.,float('inf')]]));(root/'options.json').write_text(json.dumps(dict(method='highs',options={})))
            script='import time,profile_solver;profile_solver.linprog=lambda *a,**k:time.sleep(30);profile_solver.main()'
            child=subprocess.Popen([sys.executable,'-c',script,name],stdin=subprocess.PIPE,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            try:
                child.stdin.close();self.assertEqual(child.wait(timeout=3),3)
            finally:
                if child.poll() is None:child.kill();child.wait(timeout=2)
    def test_helper_refuses_arbitrary_directory_before_reading_payload(self):
        with self.assertRaisesRegex(ValueError,'Private numeric job'):profile_solver.main(['/'])
if __name__=='__main__':unittest.main()
