using UnityEngine;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    public sealed partial class RoutePath
    {
        public readonly Vector3[] Points;
        readonly float[] distances;
        public readonly GeoRegion Region;
        public readonly bool IsClosed;
        public RoutePath(GeoRegion region,bool reverse=false) : this(region,reverse,null) { }
        RoutePath(GeoRegion region,bool reverse,string[] sourceRoadIds)
        {
            Region=region;Points=(Vector3[])region.route.Clone();if(reverse)System.Array.Reverse(Points);distances=new float[Points.Length];
            IsClosed=(Points[Points.Length-1]-Points[0]).sqrMagnitude<=.01f;
            for(int i=1;i<Points.Length;i++)distances[i]=distances[i-1]+Vector3.Distance(Points[i-1],Points[i]);
            // Circuits keep their existing surface dispatch. Ordinary mapped
            // routes bind centre-line ownership before any lane displacement.
            if(!region.circuit&&!region.AutomaticWorld)routeRoadOwnership=RoadOwnership.Create(region,Points,sourceRoadIds,reverse);
        }
        public int DrivingSide=>Region?.drivingSide??-1;
        public float LaneOffset(float d,int direction=1)
        {var centre=SourcePosition(d);var road=Region?.AutomaticWorld==true?Region.ProjectRoadForVehicle(centre,centre.y).Road:null;return Mathf.Min(2.7f,(road?.NativeTravelWidth>0?road.NativeTravelWidth:Width(d))*.24f)*direction*DrivingSide;}
        public float Length => distances[distances.Length - 1];
        public RoutePath(int count = 640)
        {
            IsClosed=true;
            Points = new Vector3[count + 1]; distances = new float[count + 1];
            for (int i = 0; i <= count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2;
                Points[i] = new Vector3(520 * Mathf.Cos(a) + 55 * Mathf.Sin(a * 3),
                    14 + 7 * Mathf.Sin(a * 2) + 2 * Mathf.Sin(a * 5), 720 * Mathf.Sin(a));
                if (i > 0) distances[i] = distances[i - 1] + Vector3.Distance(Points[i - 1], Points[i]);
            }
        }
        int Segment(float d)
        {
            int lo = 0, hi = Points.Length - 2;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (distances[mid] <= d) lo = mid; else hi = mid - 1; }
            return lo;
        }
        public Vector3 At(float distance, float lateral = 0)
        {
            if (scopedSession800 != null) return scopedSession800.Point(Mathf.Clamp(distance, 0, Length), lateral);
            distance = IsClosed?DrivingMath.Wrap(distance, Length):Mathf.Clamp(distance,0,Length);
            int i = Segment(distance);
            float t = (distance - distances[i]) / (distances[i + 1] - distances[i]);
            var forward = (Points[i + 1] - Points[i]).normalized;
            var point=Vector3.Lerp(Points[i], Points[i + 1], t);
            var sourceRoad=routeRoadOwnership?.Get(i);
            point+=Vector3.Cross(Vector3.up, forward).normalized*lateral;
            if(Region!=null&&!Region.AutomaticWorld)
            {
                // An unbound mapped segment explicitly retains its stored route
                // height. Never guess an upper/lower surface from lane-offset XZ.
                var road=routeRoadOwnership!=null?sourceRoad:Region.Nearest(point,out _,out _);
                if(road!=null&&!road.tunnel)point.y=Region.RoadSurfaceHeightForRoad(road,point.x,point.z);
            }
            return point;
        }
        Vector3 SourcePosition(float distance)
        {distance=IsClosed?DrivingMath.Wrap(distance,Length):Mathf.Clamp(distance,0,Length);int i=Segment(distance);return Vector3.Lerp(Points[i],Points[i+1],(distance-distances[i])/(distances[i+1]-distances[i]));}
        // Horizontal source geometry for bounded AI look-ahead; no terrain queries or height authority.
        public Vector3 PlanarForward(float distance){var v=SourcePosition(distance+2)-SourcePosition(distance-2);v.y=0;return v.normalized;}
        public Vector3 Forward(float distance) => (At(distance + 2) - At(distance - 2)).normalized;
        public Quaternion Rotation(float distance) => Quaternion.LookRotation(Forward(distance), Vector3.up);
        public float Closest(Vector3 p, out float lateral, out float separation)
        {
            float best = float.MaxValue, progress = 0; lateral = 0;
            for (int i = 0; i < Points.Length - 1; i++)
            {
                var a = Points[i]; var b = Points[i + 1]; var v = b - a;
                var flat = new Vector3(v.x, 0, v.z); var delta = p - a; delta.y = 0;
                float t = Mathf.Clamp01(Vector3.Dot(delta, flat) / flat.sqrMagnitude);
                var q = a + v * t; var error = p - q; error.y = 0;
                if (error.sqrMagnitude >= best) continue;
                best = error.sqrMagnitude; progress = Mathf.Lerp(distances[i], distances[i + 1], t);
                lateral = Vector3.Dot(error, Vector3.Cross(Vector3.up, v).normalized);
            }
            separation = Mathf.Sqrt(best); return progress;
        }
        public float DistanceAtPoint(int index) => distances[index];
        public float Width(float d)
        {
            if (scopedSession800 != null) return PreparedScopedSession800.DiagnosticWidthMetres;
            if(routeRoadOwnership!=null){var road=RouteRoad(d,out var centre);if(road!=null)return Region.EffectiveWidth(road,Region.RoadProgress(road,centre));var fallback=Region.ProjectRoad(centre);return fallback.Road!=null?fallback.Width:7;}
            if(Region!=null){var projection=Region.ProjectRoad(At(d));return projection.Road!=null?projection.Width:7;}
            d=DrivingMath.Wrap(d,Length);
            float exit=Mathf.SmoothStep(0,1,Mathf.Clamp01((Length*.29f-d)/100));
            float entrance=Mathf.SmoothStep(0,1,Mathf.Clamp01((d-(Length-100))/100));
            return Mathf.Lerp(12,28,Mathf.Max(exit,entrance));
        }
        public int Limit(float d)
        {
            if(routeRoadOwnership!=null){var road=RouteRoad(d,out var centre);return road!=null?road.limit:(Region.Nearest(centre,out _,out _)?.limit??30);}
            return Region!=null?(Region.Nearest(At(d),out _,out _)?.limit??30):d < Length * .29f ? 70 : d < Length * .54f ? 30 : 50;
        }
        public string District(float d) => Region!=null?Region.name:d < Length * .29f ? "COASTAL EXPRESSWAY" : d < Length * .54f ? "ASHFORD VILLAGE" : "BLACKWOOD HILLS";
        public float TerrainHeight(float x, float z)
        {
            if (scopedSession800 != null)
            {
                if (!scopedSession800.Plan.TryGround(x, z, out var ground)) throw new System.InvalidOperationException("Outside actual scoped ground support.");
                return (float)ground.Height;
            }
            if(Region!=null)return Region.TerrainHeight(x,z);
            float d = Closest(new Vector3(x, 0, z), out _, out float distance);
            float edge=Mathf.Max(Mathf.Clamp01((Mathf.Abs(x)-760)/200),Mathf.Clamp01((Mathf.Abs(z)-980)/220));
            float natural = -4 + Mathf.PerlinNoise((x + 2500) * .0018f, (z + 2500) * .0018f) * 36 + edge*edge*95;
            // A 12 m terrain cell can span the road even when none of its corners is on it.
            // Flatten a full cell diagonal beyond the verge before blending into the hills.
            // The clearance also covers interpolation over the road's vertical curves.
            return Mathf.Lerp(At(d).y - .2f, natural, Mathf.SmoothStep(0, 1, Mathf.Clamp01((distance - Width(d) * .5f - 18) / 38)));
        }
    }
}
