using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    /// <summary>Actual CancelStage fault-path unit checks on an owned empty GeoWorld.
    /// This is not a moving-world, driving or native scenery acceptance claim.</summary>
    public static class BuildingWindowLifecycleVerification
    {
        sealed class TestIterator : IEnumerator, IDisposable
        {
            readonly bool fail;
            public bool Disposed;
            public TestIterator(bool fail) { this.fail=fail; }
            public object Current => null;
            public bool MoveNext() => false;
            public void Reset() => throw new NotSupportedException();
            public void Dispose() { Disposed=true;if(fail)throw new IOException("Intentional iterator disposal failure"); }
        }
        static void Case(bool fail)
        {
            var host=new GameObject("Owned building lifecycle test");
            GameObject child=null;
            try
            {
                var world=host.AddComponent<GeoWorld>();child=new GameObject("Owned unpublished chunk");child.transform.SetParent(host.transform,false);
                var type=typeof(GeoWorld);const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                var stageType=type.GetNestedType("ChunkStage",BindingFlags.NonPublic);
                var stage=Activator.CreateInstance(stageType,true);var iterator=new TestIterator(fail);
                stageType.GetField("root").SetValue(stage,child.transform);
                // The production stage stores IEnumerator<int>; use the typed adapter below.
                stageType.GetField("work").SetValue(stage,new TypedIterator(iterator));
                type.GetField("stage",hidden).SetValue(world,stage);
                bool refused=false;
                try{type.GetMethod("CancelStage",hidden).Invoke(world,new object[]{"owned fault verification"});}
                catch(TargetInvocationException error)when(error.InnerException is AggregateException){refused=true;}
                if(refused!=fail||!iterator.Disposed||type.GetField("stage",hidden).GetValue(world)!=null||child)
                    throw new InvalidOperationException("CancelStage must detach and release even when iterator disposal fails");
            }
            finally { if(child)UnityEngine.Object.DestroyImmediate(child);UnityEngine.Object.DestroyImmediate(host); }
        }
        sealed class TypedIterator : System.Collections.Generic.IEnumerator<int>
        {
            readonly TestIterator inner;
            public TypedIterator(TestIterator value){inner=value;}
            public int Current=>0;object IEnumerator.Current=>0;
            public bool MoveNext()=>inner.MoveNext();public void Reset()=>inner.Reset();public void Dispose()=>inner.Dispose();
        }
        static GeoBuilding Rectangle(string id,float centreX,float halfWidth)
        {return new GeoBuilding{id=id,kind="house",roof="gable",height=6,points=new[]{new Vector3(centreX-halfWidth,0,95),new Vector3(centreX+halfWidth,0,95),new Vector3(centreX+halfWidth,0,105),new Vector3(centreX-halfWidth,0,105),new Vector3(centreX-halfWidth,0,95)}};}
        static void NeighbourCase(GeoBuilding owned)
        {
            var host=new GameObject("Owned building fingerprint test");
            try
            {
                var world=host.AddComponent<GeoWorld>();const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                string Signature(params GeoBuilding[] input)
                {
                    var plan=BuildingRenderInput.Create(input);var index=new System.Collections.Generic.Dictionary<Vector2Int,System.Collections.Generic.List<GeoBuilding>>();
                    var add=typeof(GeoWorld).GetMethod("Index",hidden).MakeGenericMethod(typeof(GeoBuilding));
                    foreach(var entry in plan.Renderable)add.Invoke(world,new object[]{index,entry.Geometry,entry.Geometry.points,0f});
                    return (string)typeof(GeoWorld).GetMethod("BuildingFingerprint",hidden).Invoke(world,new object[]{new Vector2Int(0,0),plan,index});
                }
                string baseline=Signature(owned),distant=Signature(owned,Rectangle("far-neighbour",350,2));
                var near=Rectangle("near-neighbour",owned.points[1].x+3,1);
                if(baseline!=distant||baseline==Signature(owned,near))throw new InvalidOperationException("Fingerprint must ignore distant same-cell neighbours but retain actual cross-cell clearance dependencies");
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        public static void RunAndBuild()
        {
            try
            {
                Case(false);Case(true);NeighbourCase(Rectangle("owner",314,5));NeighbourCase(Rectangle("overhang-owner",315,18));
                var arguments=Environment.GetCommandLineArgs();int at=Array.IndexOf(arguments,"-roadRushBuildingLifecycleResult");
                if(at<0||at+1>=arguments.Length)throw new ArgumentException("Explicit lifecycle receipt required");
                File.WriteAllText(arguments[at+1],"PASS actual GeoWorld.CancelStage normal and throwing-Dispose cases; stage detached, iterator disposed, unpublished root released. Cross-cell near/far and whole-footprint overhang fingerprints PASS. Editor unit scope only.\n");
                LoadingCameraQaBuild.Build();
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
