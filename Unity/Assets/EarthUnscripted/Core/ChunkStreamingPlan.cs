using System;
using System.Collections.Generic;
using System.Linq;
namespace EarthUnscripted.Core
{
    public readonly struct ChunkCell : IEquatable<ChunkCell>,IComparable<ChunkCell>
    {
        public readonly int X,Z;public ChunkCell(int x,int z){X=x;Z=z;}
        public bool Equals(ChunkCell other)=>X==other.X&&Z==other.Z;
        public override bool Equals(object obj)=>obj is ChunkCell other&&Equals(other);
        public override int GetHashCode()=>unchecked(X*73856093^Z*19349663);
        public int CompareTo(ChunkCell other){int x=X.CompareTo(other.X);return x!=0?x:Z.CompareTo(other.Z);}
        public override string ToString()=>X+","+Z;
    }
    public readonly struct ChunkMotion
    {
        public readonly double X,Z,VelocityX,VelocityZ;
        public ChunkMotion(double x,double z,double vx,double vz){X=x;Z=z;VelocityX=vx;VelocityZ=vz;}
        // Continuous ray/AABB entry time for this cell becoming part of the
        // original7x7required window; infinity when motion cannot reach it.
        public double Deadline(ChunkCell key)
        {
            double enter=0,exit=double.PositiveInfinity;
            bool Axis(double p,double v,double min,double max)
            {
                if(Math.Abs(v)<.000001)return p>=min&&p<max;
                if((p>=max&&v>=0)||(p<min&&v<=0))return false;
                double a=(min-p)/v,b=(max-p)/v;if(a>b){double swap=a;a=b;b=swap;}enter=Math.Max(enter,a);exit=Math.Min(exit,b);return enter<=exit&&exit>=0;
            }
            return Axis(X,VelocityX,((double)key.X-3)*320,((double)key.X+4)*320)&&Axis(Z,VelocityZ,((double)key.Z-3)*320,((double)key.Z+4)*320)?Math.Max(0,enter):double.PositiveInfinity;
        }
    }
    // Scheduling only. Geometry, collision surfaces and the pinned region remain
    // outside this planner and are never simplified to meet a work budget.
    public sealed class ChunkStreamingPlan
    {
        public const int RequiredRadius=3,ResidentRadius=4;
        readonly ChunkCell[] origins;readonly HashSet<ChunkCell> required,resident;
        public int OriginCount=>origins.Length;public int RequiredCount=>required.Count;public int ResidentCount=>resident.Count;
        public IEnumerable<ChunkCell> Required=>required;
        public IEnumerable<ChunkCell> Resident=>resident;
        public ChunkStreamingPlan(IEnumerable<ChunkCell> points,Func<ChunkCell,bool> intersects)
        {
            origins=points.Distinct().OrderBy(p=>p).ToArray();required=Window(origins,RequiredRadius,intersects);resident=Window(origins,ResidentRadius,intersects);
        }
        static HashSet<ChunkCell> Window(IEnumerable<ChunkCell> origins,int radius,Func<ChunkCell,bool> intersects)
        {
            var result=new HashSet<ChunkCell>();foreach(var origin in origins)for(int z=-radius;z<=radius;z++)for(int x=-radius;x<=radius;x++)
            {
                long xx=(long)origin.X+x,zz=(long)origin.Z+z;if(xx<int.MinValue||xx>int.MaxValue||zz<int.MinValue||zz>int.MaxValue)continue;var key=new ChunkCell((int)xx,(int)zz);if(intersects(key))result.Add(key);
            }
            return result;
        }
        public static ChunkCell[] NearBoundaryFrontier(double x,double z,Func<ChunkCell,bool> intersects,double margin=64)
        {
            if(double.IsNaN(x)||double.IsInfinity(x)||double.IsNaN(z)||double.IsInfinity(z)||margin<0||margin>=160)throw new ArgumentOutOfRangeException();
            var center=new ChunkCell((int)Math.Floor(x/320),(int)Math.Floor(z/320));double fx=x-center.X*320d,fz=z-center.Z*320d;
            var xs=new List<int>{0};var zs=new List<int>{0};if(fx<=margin)xs.Add(-1);if(fx>=320-margin)xs.Add(1);if(fz<=margin)zs.Add(-1);if(fz>=320-margin)zs.Add(1);
            var origins=new List<ChunkCell>();foreach(int xx in xs)foreach(int zz in zs)origins.Add(new ChunkCell(center.X+xx,center.Z+zz));
            var baseWindow=Window(new[]{center},RequiredRadius,intersects);return Window(origins,RequiredRadius,intersects).Where(k=>!baseWindow.Contains(k)).OrderBy(k=>k).ToArray();
        }
        public bool SameOrigins(IEnumerable<ChunkCell> points)=>origins.SequenceEqual(points.Distinct().OrderBy(p=>p));
        public bool IsRequired(ChunkCell key)=>required.Contains(key);
        public bool IsResident(ChunkCell key)=>resident.Contains(key);
        public ChunkCell[] MissingRequired(ISet<ChunkCell> loaded)=>Sort(required.Where(key=>!loaded.Contains(key)));
        public ChunkCell[] MissingPrefetch(ISet<ChunkCell> loaded)=>Sort(resident.Where(key=>!loaded.Contains(key)));
        public ChunkCell[] MissingPrefetch(ISet<ChunkCell> loaded,IEnumerable<ChunkMotion> observers)
        {
            var motion=observers.ToArray();if(motion.Length==0)return MissingPrefetch(loaded);
            return resident.Where(key=>!loaded.Contains(key)).OrderBy(key=>motion.Min(o=>o.Deadline(key))).ThenBy(Distance).ThenBy(SquaredDistance).ThenBy(key=>key).ToArray();
        }
        public ChunkCell[] Retire(ISet<ChunkCell> loaded)=>loaded.Where(key=>!resident.Contains(key)).OrderBy(key=>key).ToArray();
        public int MissingRequiredCount(ISet<ChunkCell> loaded)=>required.Count(key=>!loaded.Contains(key));
        ChunkCell[] Sort(IEnumerable<ChunkCell> keys)=>keys.OrderBy(Distance).ThenBy(SquaredDistance).ThenBy(key=>key).ToArray();
        long Distance(ChunkCell key)=>origins.Length==0?long.MaxValue:origins.Min(o=>Math.Max(Math.Abs((long)key.X-o.X),Math.Abs((long)key.Z-o.Z)));
        double SquaredDistance(ChunkCell key)=>origins.Length==0?double.MaxValue:origins.Min(o=>{double x=(double)key.X-o.X,z=(double)key.Z-o.Z;return x*x+z*z;});
    }
}
