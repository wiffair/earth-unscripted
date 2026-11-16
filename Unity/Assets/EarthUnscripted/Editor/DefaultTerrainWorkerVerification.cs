using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public static class DefaultTerrainWorkerVerification
    {
        static void Require(bool value,string message){if(!value)throw new Exception(message);}
        static int Bits(float value)=>BitConverter.ToInt32(BitConverter.GetBytes(value),0);
        public static void Run()
        {
            string output=Path.Combine("Builds/Verification/DefaultTerrainWorker",DateTime.UtcNow.Ticks.ToString());Directory.CreateDirectory(output);
            try
            {
                int main=Thread.CurrentThread.ManagedThreadId,worker=main;
                var route=new RoutePath();var vertices=new Vector3[161*201];var uv=new Vector2[vertices.Length];var triangles=new int[160*200*6];int k=0;
                // Independent preserved original loop, including its original index arrays.
                for(int z=0;z<=200;z++)for(int x=0;x<=160;x++){int i=z*161+x;float wx=(x-80)*12,wz=(z-100)*12;vertices[i]=new Vector3(wx,route.TerrainHeight(wx,wz),wz);uv[i]=new Vector2(wx/12,wz/12);}
                for(int z=0;z<200;z++)for(int x=0;x<160;x++){int a=z*161+x;int[] t={a,a+161,a+1,a+1,a+161,a+162};foreach(int v in t)triangles[k++]=v;}
                var plan=Task.Run(()=>{worker=Thread.CurrentThread.ManagedThreadId;return DefaultTerrainPlan.Prepare(route,CancellationToken.None);}).GetAwaiter().GetResult();
                Require(worker!=main,"Numeric terrain did not execute on a worker");Require(ReferenceEquals(route,plan.Owner),"Route ownership lost");
                Require(plan.Vertices.Length==vertices.Length&&plan.UV.Length==uv.Length&&plan.Triangles.Length==triangles.Length,"Geometry counts changed");
                for(int i=0;i<vertices.Length;i++)
                {
                    var a=vertices[i];var b=plan.Vertices[i];Require(Bits(a.x)==Bits(b.x)&&Bits(a.y)==Bits(b.y)&&Bits(a.z)==Bits(b.z),"Vertex bits changed at "+i);
                    Require(Bits(uv[i].x)==Bits(plan.UV[i].x)&&Bits(uv[i].y)==Bits(plan.UV[i].y),"UV bits changed at "+i);
                }
                for(int i=0;i<triangles.Length;i++)Require(triangles[i]==plan.Triangles[i],"Triangle order changed at "+i);
                using(var cancelled=new CancellationTokenSource())
                {cancelled.Cancel();bool rejected=false;try{DefaultTerrainPlan.Prepare(route,cancelled.Token);}catch(OperationCanceledException){rejected=true;}Require(rejected,"Cancelled preparation returned terrain");}
                var prepared=Task.Run(()=>WorldDataFinalizer.PrepareOwned(null,null,CancellationToken.None)).GetAwaiter().GetResult();
                Require(prepared.Region==null&&prepared.DefaultTerrain!=null&&ReferenceEquals(prepared.Route,prepared.DefaultTerrain.Owner),"Finalizer failed owned default terrain transfer");
                Require(prepared.DefaultTerrain.Vertices.Length==32361&&prepared.DefaultTerrain.Triangles.Length==192000,"Finalizer geometry bounds");
                File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: actual worker thread,32361 bit-identical vertices+UVs,192000 exact indices,pre-cancel rejection,owned finalizer transfer. No mesh generation, native performance or scene visual acceptance claimed.");
                Debug.Log("DEFAULT TERRAIN WORKER PASS "+Path.GetFullPath(output));EditorApplication.Exit(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
