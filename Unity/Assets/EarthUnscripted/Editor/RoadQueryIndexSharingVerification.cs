using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class RoadQueryIndexSharingVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type Region=typeof(GeoRegion),Segment=Region.GetNestedType("Segment",BindingFlags.NonPublic);
        static readonly FieldInfo Source=Region.GetField("sourceCells",Hidden),Render=Region.GetField("cells",Hidden),Prepared=Region.GetField("preparedRoadWidths",Hidden);
        static readonly MethodInfo SourceQuery=Region.GetMethod("NearestCore",Hidden),CanShare=Region.GetMethod("CanShareRoadQueryIndex",Hidden);
        static int checks;
        static void Check(bool pass,string why){if(!pass)throw new Exception("ROAD QUERY SHARING FAIL "+why);checks++;}
        static GeoRegion Fixture()
        {
            var roads=new List<GeoRoad>();
            for(int r=0;r<26;r++)
            {
                var points=Enumerable.Range(0,23).Select(i=>r%2==0?new Vector3(-220+i*20,10+r*.05f,r==0?30:r*7-90):new Vector3(r*7-90,10+r*.05f,-220+i*20)).ToArray();
                roads.Add(new GeoRoad{id="query-"+r,start="start-"+r,end="end-"+r,width=r%3==0?12:8,kind="residential",points=points});
            }
            // Equal-score candidates exercise first-insertion tie behaviour.
            roads.Add(new GeoRoad{id="tie",start="tie-a",end="tie-b",width=12,kind="residential",points=(Vector3[])roads[0].points.Clone()});
            roads.Add(new GeoRoad{id="upper",start="upper-a",end="upper-b",width=10,bridge=true,layer=1,points=new[]{new Vector3(-200,25,30),new Vector3(200,25,30)}});
            roads.Add(new GeoRoad{id="underground",start="under-a",end="under-b",width=10,tunnel=true,layer=-1,points=new[]{new Vector3(-200,-5,30),new Vector3(200,-5,30)}});
            return new GeoRegion{roads=roads.ToArray(),waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),minX=-400,minZ=-400,width=800,depth=800,nx=2,nz=2,heights=new[]{18f,18f,18f,18f}};
        }
        // Independent pre-optimization construction of BOTH original indexes.
        // This does not invoke the production sharing predicate or reuse its lists.
        static IDictionary OriginalIndex(GeoRegion region,bool render)
        {
            var index=(IDictionary)Activator.CreateInstance(Source.FieldType);
            foreach(var road in region.roads)
            {
                float start=0;
                for(int i=1;i<road.points.Length;i++)
                {
                    var a=road.points[i-1];var b=road.points[i];float length=Vector3.Distance(a,b);
                    float maximum=region.PreparedRoadWidths==null?road.width:region.PreparedRoadWidths.MaximumBetween(road,start,start+length);
                    var segment=Activator.CreateInstance(Segment);
                    foreach(var pair in new Dictionary<string,object>{{"road",road},{"a",a},{"b",b},{"start",start},{"length",length},{"maxWidth",maximum}})
                        Segment.GetField(pair.Key).SetValue(segment,pair.Value);
                    float margin=render?Mathf.Max(48,maximum*.5f+18):48;
                    for(int z=Mathf.FloorToInt((Mathf.Min(a.z,b.z)-margin)/80);z<=Mathf.FloorToInt((Mathf.Max(a.z,b.z)+margin)/80);z++)
                    for(int x=Mathf.FloorToInt((Mathf.Min(a.x,b.x)-margin)/80);x<=Mathf.FloorToInt((Mathf.Max(a.x,b.x)+margin)/80);x++)
                    {
                        var key=new Vector2Int(x,z);
                        if(!index.Contains(key))index.Add(key,Activator.CreateInstance(typeof(List<>).MakeGenericType(Segment)));
                        ((IList)index[key]).Add(segment);
                    }
                    start+=length;
                }
            }
            return index;
        }
        static bool SameIndex(IDictionary a,IDictionary b)
        {
            if(a.Count!=b.Count)return false;
            foreach(DictionaryEntry cell in a)
            {
                if(!b.Contains(cell.Key))return false;
                var aa=(IList)cell.Value;var bb=(IList)b[cell.Key];if(aa.Count!=bb.Count)return false;
                for(int i=0;i<aa.Count;i++)foreach(var f in Segment.GetFields())
                {
                    object av=f.GetValue(aa[i]),bv=f.GetValue(bb[i]);
                    if(f.Name=="road"){if(((GeoRoad)av).id!=((GeoRoad)bv).id)return false;}
                    else if(!Equals(av,bv))return false;
                }
            }
            return true;
        }
        static long Capacity(IDictionary index)
        {long result=0;foreach(DictionaryEntry cell in index)result+=(int)cell.Value.GetType().GetProperty("Capacity").GetValue(cell.Value);return result;}
        static bool SameSourceProjection(GeoRegion actual,GeoRegion baseline,Vector3 point)
        {
            object[] a={point,null,0f,0f,0f,true,true,true},b={point,null,0f,0f,0f,true,true,true};
            var ar=(GeoRoad)SourceQuery.Invoke(actual,a);var br=(GeoRoad)SourceQuery.Invoke(baseline,b);
            return ar?.id==br?.id&&Equals(a[1],b[1])&&Equals(a[2],b[2])&&Equals(a[3],b[3])&&Equals(a[4],b[4]);
        }
        static void Compare(GeoRegion actual,GeoRegion baseline,string label)
        {
            baseline.RebuildRenderQueryIndexesOnly(true);
            Source.SetValue(baseline,OriginalIndex(baseline,false));Render.SetValue(baseline,OriginalIndex(baseline,true));
            Check(!ReferenceEquals(Source.GetValue(baseline),Render.GetValue(baseline)),label+" baseline retains independently constructed indexes");
            Check(SameIndex((IDictionary)Source.GetValue(actual),(IDictionary)Source.GetValue(baseline)),label+" all source candidates and their insertion order match original construction");
            Check(SameIndex((IDictionary)Render.GetValue(actual),(IDictionary)Render.GetValue(baseline)),label+" all render candidates and their insertion order match original construction");
            bool queries=true;var random=new System.Random(51043);
            for(int i=0;i<240;i++)
            {
                var p=i==0?new Vector3(0,10,30):new Vector3((float)random.NextDouble()*640-320,10,(float)random.NextDouble()*640-320);
                var a=actual.ProjectRoad(p);var b=baseline.ProjectRoad(p);
                queries&=a.Road?.id==b.Road?.id&&a.Point==b.Point&&a.Separation==b.Separation&&a.SourceDistance==b.SourceDistance&&a.Width==b.Width;
                queries&=SameSourceProjection(actual,baseline,p);
                queries&=actual.RoadSurfaceHeight(p.x,p.z)==baseline.RoadSurfaceHeight(p.x,p.z)&&actual.RoadSurfaceHeightOnLayer(p.x,p.z,0)==baseline.RoadSurfaceHeightOnLayer(p.x,p.z,0);
                queries&=actual.TerrainHeight(p.x,p.z)==baseline.TerrainHeight(p.x,p.z);
            }
            Check(queries,label+" 240 render/source projections and surface/layer/terrain queries remain exact");
        }
        static void SetProfiles(GeoRegion region)
        {
            var profiles=region.roads.Where(r=>!r.tunnel).Select(r=>new RoadWidthProfileDto{roadId=r.id,distances=new[]{0f,r.length},widths=new[]{r.id=="query-0"?80:r.width,r.id=="query-0"?80:r.width},derivatives=new[]{0f,0f}}).ToArray();
            Prepared.SetValue(region,new EffectiveRoadWidthField(profiles,region.roads));
        }
        public static void Run()
        {
            checks=0;var actual=Fixture();actual.RebuildRenderQueryIndexesOnly(true);
            Check(ReferenceEquals(Source.GetValue(actual),Render.GetValue(actual)),"eligible narrow road indexes share exactly one dictionary");
            Compare(actual,Fixture(),"narrow");long saved=Capacity((IDictionary)Source.GetValue(actual));
            Check(saved>2000,"fixture avoids a real second segment backing allocation");
            actual.RebuildRenderQueryIndexesOnly(true);Compare(actual,Fixture(),"repeated narrow rebuild");
            Check(ReferenceEquals(Source.GetValue(actual),Render.GetValue(actual)),"repeated rebuild preserves shared ownership without duplicate insertion");
            actual.roads[0].width=100;actual.RebuildRenderQueryIndexesOnly(true);var wide=Fixture();wide.roads[0].width=100;
            Check(!ReferenceEquals(Source.GetValue(actual),Render.GetValue(actual)),"wide road detaches aliased render dictionary");Compare(actual,wide,"wide transition");
            Check(!SameIndex((IDictionary)Source.GetValue(actual),(IDictionary)Render.GetValue(actual)),"wide fixture genuinely requires different bounds");
            actual.roads[0].width=12;actual.RebuildRenderQueryIndexesOnly(true);Compare(actual,Fixture(),"return to narrow");
            Check(ReferenceEquals(Source.GetValue(actual),Render.GetValue(actual)),"narrow road rebuild can share again");
            SetProfiles(actual);var profiled=Fixture();profiled.RebuildRenderQueryIndexesOnly(true);SetProfiles(profiled);
            actual.RebuildRenderQueryIndexesOnly(true);Compare(actual,profiled,"prepared widths");
            Check(!ReferenceEquals(Source.GetValue(actual),Render.GetValue(actual)),"prepared widths retain independent indexes");
            Prepared.SetValue(actual,null);actual.RebuildRenderQueryIndexesOnly(true);Compare(actual,Fixture(),"profile removal");
            Check(ReferenceEquals(Source.GetValue(actual),Render.GetValue(actual)),"removing prepared widths safely restores sharing");
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,60.001f})
            {actual.roads[0].width=invalid;Check(!(bool)CanShare.Invoke(actual,null),"unproven width cannot share: "+invalid);}
            actual.roads[0].width=60;Check((bool)CanShare.Invoke(actual,null),"finite 60m boundary has exactly equal bounds");
            Debug.Log("ROAD QUERY SHARING PASS "+checks+"; saved segment backing capacity="+saved+" slots per narrow fixture snapshot; 6 x 240 exact query comparisons");
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
