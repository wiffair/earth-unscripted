using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class MacWorkPriorityVerification
    {
        public static int Exercise()
        {
            int checks=0;void Check(bool value,string why){if(!value)throw new Exception("MAC URGENT QOS "+why);checks++;}
            Check(MacWorkPriority.IsRoadCollider("Road asphalt")&&MacWorkPriority.IsRoadCollider("Terrain")&&MacWorkPriority.IsRoadCollider("Bridge decks / barriers"),"Only named drivable terrain/road structural collider categories are eligible");
            foreach(var name in new[]{"Mapped roofs","Mapped building massing","Railway ballast","Footpaths","random",null})Check(!MacWorkPriority.IsRoadCollider(name),"Optional/background collider is not elevated: "+name);
            bool rejected=false;try{MacWorkPriority.RunUrgent((Action)null,"invalid");}catch(ArgumentNullException){rejected=true;}Check(rejected,"Null work rejects before scheduling");
            var before=MacWorkPriority.Capture();
            var first=MacWorkPriority.RunUrgent(()=>{long value=0;for(int i=0;i<100000;i++)value+=i;return value;},"qa.priority-result");Check(first.GetAwaiter().GetResult()==4999950000L,"Actual pooled worker retains its complete numeric result");
            var failure=MacWorkPriority.RunUrgent((Action)(()=>throw new InvalidOperationException("expected scoped failure")),"qa.priority-exception");
            rejected=false;try{failure.GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}Check(rejected&&failure.IsFaulted,"Work failure propagates after scope cleanup");
            Task.WaitAll(Enumerable.Range(0,4).Select(i=>MacWorkPriority.RunUrgent(()=>i*i,"qa.priority-concurrent")).ToArray());
            var after=MacWorkPriority.Capture();Check(after.releaseFailures==before.releaseFailures,"Actual native override cleanup returned no error");
            Check(after.applied-before.applied==after.ended-before.ended,"Every acquired native override ended, including exception and concurrent paths");
            if(!MacWorkPriority.NativePlatform)Check(after.applied==0,"Non-Mac paths never acquire a Darwin override");
            if(MacWorkPriority.Recording)
            {var rows=after.receipts.Where(r=>r.operation.StartsWith("qa.priority-",StringComparison.Ordinal)).ToArray();Check(rows.Length>=6,"Explicit proof receipt captures real worker tasks");Check(rows.All(r=>(!MacWorkPriority.NativePlatform||r.requestedBeforeStatus!=0||r.requestedAfterStatus!=0)||(r.requestedBefore==r.requestedAfter&&r.relativeBefore==r.relativeAfter)),"Pooled thread requested/base QoS is unchanged after the native scope");Check(rows.All(r=>r.workerMilliseconds>=0&&r.queuedMilliseconds>=0),"Worker and queue clocks are monotonic measurements");}
            Debug.Log("MAC URGENT QOS PASS "+checks+" platform="+MacWorkPriority.NativePlatform+" requested="+MacWorkPriority.Requested+" applied="+(after.applied-before.applied)+"; requested QoS does not expose effective override or CPU-core residency");return checks;
        }
        public static void Run(){Exercise();}
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
