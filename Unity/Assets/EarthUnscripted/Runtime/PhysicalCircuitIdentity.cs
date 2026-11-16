using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Worker-only physical course identity for model/course economy records.
    /// Excludes names, event/rule tokens and scenery. Includes finalized surface samples,
    /// effective widths and exact mesh vertices/indices where that surface representation exists.</summary>
    public static class PhysicalCircuitIdentity
    {
        public static string Compute(PreparedWorldData world,CancellationToken cancellation=default)
        {
            if(world?.Region==null||!world.Region.circuit)return null;
            var region=world.Region;var route=world.Route;
            using(var bytes=new MemoryStream())
            {
                using(var writer=new BinaryWriter(bytes,new UTF8Encoding(false,true),true))
                {
                    writer.Write("EarthUnscriptedPhysicalCircuit1");writer.Write(region.latitude);writer.Write(region.longitude);writer.Write(region.datum);
                    void Number(float value){if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Non-finite circuit identity input");writer.Write(value==0?0:value);}
                    void Point(Vector3 value){Number(value.x);Number(value.y);Number(value.z);}
                    writer.Write(route.Points.Length);float arc=0;
                    for(int i=0;i<route.Points.Length;i++)
                    {
                        cancellation.ThrowIfCancellationRequested();if(i>0)arc+=Vector3.Distance(route.Points[i-1],route.Points[i]);
                        Point(route.Points[i]);Point(route.At(arc));Number(route.Width(arc));
                    }
                    // Preserve identities for every existing course. New authored
                    // geometry contributes its physical triangles, including pits.
                    if(region.CircuitRoadSurfaces!=null)
                    {
                        writer.Write("authored-pavement1");writer.Write(region.CircuitRoadSurfaces.Parts.Count);
                        foreach(var part in region.CircuitRoadSurfaces.Parts)
                        {
                            cancellation.ThrowIfCancellationRequested();writer.Write(part.Vertices.Count);
                            foreach(var point in part.Vertices)Point(point);
                            writer.Write(part.Triangles.Count);foreach(int index in part.Triangles)writer.Write(index);
                        }
                    }
                    var roads=region.roads.Where(r=>r.kind=="raceway").ToArray();writer.Write(roads.Length);
                    foreach(var road in roads)
                    {
                        cancellation.ThrowIfCancellationRequested();writer.Write(road.bridge);writer.Write(road.tunnel);writer.Write(road.layer);writer.Write(road.points.Length);arc=0;
                        for(int i=0;i<road.points.Length;i++)
                        {
                            if(i>0)arc+=Vector3.Distance(road.points[i-1],road.points[i]);var point=road.points[i];Point(point);Number(region.EffectiveWidth(road,arc));
                            Number(region.RoadSurfaceHeightForRoad(road,point.x,point.z));
                        }
                        bool mesh=world.RoadSurfaces!=null&&world.RoadSurfaces.Parts.TryGetValue(road.id,out _);writer.Write(mesh);
                        if(mesh){var surface=world.RoadSurfaces.Parts[road.id].Surface;writer.Write(surface.Vertices.Length);foreach(var v in surface.Vertices)Point(v);writer.Write(surface.Triangles.Length);foreach(var index in surface.Triangles)writer.Write(index);}
                    }
                }
                cancellation.ThrowIfCancellationRequested();using(var sha=SHA256.Create())return "physical-circuit1-"+BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();
            }
        }
    }
}
