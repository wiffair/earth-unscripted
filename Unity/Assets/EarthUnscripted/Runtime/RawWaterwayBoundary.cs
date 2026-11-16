using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// Raw map providers return whole waterways intersecting a requested area. Clip
    /// their off-area tails before bounded boot validation. This owns only the decoded
    /// copy: source files and authoritative/signed peer snapshots remain unchanged.
    /// Disconnected clipped pieces stay separate so a missing reach is never joined.
    /// </summary>
    public static class RawWaterwayBoundary
    {
        public const float GeometryMarginMetres = 4096f;
        const int MaximumWaterways = 1000, MaximumWayPoints = 10000, MaximumTotalPoints = 400000;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static void ClipOwned(GeoRegion region)
        {
            if (region == null || region.waters == null || region.waters.Length > MaximumWaterways)
                throw new InvalidDataException("Invalid raw map waterway count");
            if (!Finite(region.minX) || !Finite(region.minZ) || !Finite(region.width) || !Finite(region.depth)
                || region.width < 100 || region.width > 10000 || region.depth < 100 || region.depth > 10000)
                throw new InvalidDataException("Invalid raw map clipping bounds");
            float minX=region.minX-GeometryMarginMetres, maxX=region.minX+region.width+GeometryMarginMetres;
            float minZ=region.minZ-GeometryMarginMetres, maxZ=region.minZ+region.depth+GeometryMarginMetres;
            var output=new List<GeoWater>(); int total=0; bool changed=false;
            foreach (var water in region.waters)
            {
                if (water == null || !Finite(water.width) || water.width < 0 || water.width > 500
                    || water.points == null || water.points.Length < 2 || water.points.Length > MaximumWayPoints
                    || (total += water.points.Length) > MaximumTotalPoints)
                    throw new InvalidDataException("Invalid raw map waterway metadata/budget");
                bool inside=true;
                foreach (var point in water.points)
                {
                    if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z) || Math.Abs(point.y)>=20000)
                        throw new InvalidDataException("Invalid raw map waterway coordinate");
                    inside &= point.x>=minX && point.x<=maxX && point.z>=minZ && point.z<=maxZ;
                }
                if (inside) { output.Add(water); continue; }
                changed=true; List<Vector3> piece=null;
                void Finish()
                {
                    if (piece != null && piece.Count >= 2)
                    {
                        if (output.Count >= MaximumWaterways) throw new InvalidDataException("Clipped waterway count exceeds budget");
                        output.Add(new GeoWater { name=water.name, width=water.width, points=piece.ToArray() });
                    }
                    piece=null;
                }
                for (int i=1; i<water.points.Length; i++)
                {
                    var a=water.points[i-1];var b=water.points[i];double enter=0,exit=1;
                    double dx=(double)b.x-a.x,dz=(double)b.z-a.z;
                    bool Edge(double p,double q)
                    {
                        if (p==0) return q>=0;
                        double t=q/p;
                        if (p<0) { if(t>exit)return false; enter=Math.Max(enter,t); }
                        else { if(t<enter)return false; exit=Math.Min(exit,t); }
                        return true;
                    }
                    if (!Edge(-dx,a.x-minX)||!Edge(dx,maxX-a.x)||!Edge(-dz,a.z-minZ)||!Edge(dz,maxZ-a.z)) { Finish(); continue; }
                    Vector3 At(double t) => new Vector3(Mathf.Clamp((float)(a.x+dx*t),minX,maxX),
                        (float)(a.y+((double)b.y-a.y)*t),Mathf.Clamp((float)(a.z+dz*t),minZ,maxZ));
                    var start=At(enter);var end=At(exit);
                    if ((end-start).sqrMagnitude<=1e-10f) { Finish(); continue; }
                    if (piece==null || piece[piece.Count-1]!=start) { Finish();piece=new List<Vector3>{start}; }
                    piece.Add(end);
                    if (exit<1) Finish();
                }
                Finish();
            }
            if (output.Count > MaximumWaterways) throw new InvalidDataException("Clipped waterway budget");
            if (changed) region.waters=output.ToArray();
        }
    }
}
