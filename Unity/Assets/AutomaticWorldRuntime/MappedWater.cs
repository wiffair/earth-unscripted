using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        [Serializable] public sealed class WaterArea
        {public string id,name,levelSource;public float level;public Vector3[] outline,vertices;public CoastRing[] holes;}
        readonly Dictionary<Map,GameObject> mappedWaterRoots=new Dictionary<Map,GameObject>();
        Material mappedWaterMaterial;
        public int MappedWaterSurfaces=>mappedWaterRoots.Count;
        public bool TryMappedWaterLevel(Vector3 point,out float level)
        {
            level=float.NegativeInfinity;var owner=AreaAt(point.x,point.z);
            var candidates=owner?.waterAreas??areas.Values.SelectMany(map=>map.waterAreas??Array.Empty<WaterArea>()).ToArray();
            foreach(var area in candidates)
            {
                if(!InCoastRing(point,area.outline))continue;
                if((area.holes??Array.Empty<CoastRing>()).Any(h=>InCoastRing(point,h.points)))continue;
                level=Mathf.Max(level,area.level);
            }
            return !float.IsNegativeInfinity(level);
        }
        IEnumerator MappedWaterLoop()
        {
            while(true)
            {
                foreach(var map in areas.Values.ToArray())
                {
                    if(mappedWaterRoots.ContainsKey(map)||map.waterAreas==null||map.waterAreas.Length==0)continue;
                    if(!mappedWaterMaterial)
                    {
                        mappedWaterMaterial=EarthUnscripted.GameGraphics.Create(Resources.Load<Shader>("CoastalSurface"));
                        mappedWaterMaterial.name="Mapped inland water";mappedWaterMaterial.SetFloat("_Mode",0);mappedWaterMaterial.SetFloat("_WaveHeight",.025f);
                    }
                    var root=new GameObject("Mapped lakes and reservoirs");root.SetActive(false);root.transform.SetParent(transform,false);ownedRoots.Add(root);
                    var batch=new Batch("Inland water surface",mappedWaterMaterial,false);var watch=System.Diagnostics.Stopwatch.StartNew();
                    foreach(var body in map.waterAreas)for(int i=0;i+2<(body.vertices?.Length??0);i+=3)
                    {
                        var a=body.vertices[i];var b=body.vertices[i+1];var c=body.vertices[i+2];
                        if(Vector3.Cross(b-a,c-a).y<0)batch.Triangle(a,c,b);else batch.Triangle(a,b,c);
                        if(watch.Elapsed.TotalMilliseconds>1.5){yield return null;watch.Restart();}
                    }
                    if(!spatialByMap.ContainsKey(map)){ownedRoots.Remove(root);DestroyChunk(root);continue;}
                    batch.Publish(root.transform,new ChunkTiming());
                    foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;
                    root.SetActive(true);mappedWaterRoots[map]=root;
                    yield return null;
                }
                foreach(var entry in mappedWaterRoots.ToArray())if(!spatialByMap.ContainsKey(entry.Key))
                {ownedRoots.Remove(entry.Value);DestroyChunk(entry.Value);mappedWaterRoots.Remove(entry.Key);}
                yield return new WaitForSecondsRealtime(.5f);
            }
        }
    }
}
