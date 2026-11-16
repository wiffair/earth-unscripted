using System;
using System.Collections.Generic;
using System.Linq;
using EarthUnscripted.Candidates.Articulated;
using EarthUnscripted.Candidates.Compatibility;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class RailTrainSystem
    {
        double[] publishedMap,localMap;
        public static bool PublishedSection(PublishedTrainPath path,GeoRegion region,Vector3[] input,out Vector3[] points,out double[] global,out double[] local)
        {
            points=null;global=null;local=null;if(input==null||input.Length<2)return false;
            var data=new List<Tuple<Vector3,double>>();List<Tuple<Vector3,double>> longest=null;
            foreach(var point in input)
            {
                double at=path.Project(path.DatasetPoint(region,point),out float gap);
                if(gap>15||at<.01||at>path.Metres[path.Metres.Length-1]-.01)
                {if(data.Count>1&&(longest==null||data.Count>longest.Count))longest=data;data=new List<Tuple<Vector3,double>>();continue;}
                if(data.Count>0&&(point-data[data.Count-1].Item1).sqrMagnitude<.001f)continue;
                data.Add(Tuple.Create(point,at));
            }
            if(data.Count>1&&(longest==null||data.Count>longest.Count))longest=data;data=longest;
            if(data==null||data.Count<2)return false;if(data[data.Count-1].Item2<data[0].Item2)data.Reverse();
            var distance=new double[data.Count];
            for(int i=1;i<data.Count;i++)
            {
                var delta=data[i].Item1-data[i-1].Item1;float flat=new Vector2(delta.x,delta.z).magnitude;
                double advance=data[i].Item2-data[i-1].Item2;
                if(flat<.001f||Math.Abs(delta.y)/flat>.08||advance<.01||advance>flat*1.3+1)return false;
                distance[i]=distance[i-1]+delta.magnitude;
            }
            points=data.Select(v=>v.Item1).ToArray();global=data.Select(v=>v.Item2).ToArray();local=distance;return true;
        }
        static double ConvertMetres(double value,double[] source,double[] target)
        {
            int i=Array.BinarySearch(source,value);if(i>=0)return target[i];i=Math.Max(1,Math.Min(source.Length-1,~i));
            return target[i-1]+(target[i]-target[i-1])*(value-source[i-1])/(source[i]-source[i-1]);
        }
        TrainServicePosition LocalSample(TrainServicePosition value)
        {
            if(!value.Available||publishedMap==null)return value;
            double metres=ConvertMetres(value.Metres,publishedMap,localMap),next=double.IsFinite(value.NextStop)?ConvertMetres(value.NextStop,publishedMap,localMap):value.NextStop;
            return new TrainServicePosition(true,metres,value.Speed,value.Dwelling,next,value.Quality,value.ReportAge);
        }
        sealed class PublishedChain
        {public readonly List<Vector3> Points=new List<Vector3>();public readonly List<double> Global=new List<double>();public readonly HashSet<string> Ways=new HashSet<string>();}
        IEnumerable<PublishedChain> PublishedChains(PublishedTrainPath path)
        {
            var sections=new List<PublishedChain>();var region=session.World.Route.Region;
            foreach(var group in renderer.ResidentAreas.SelectMany(area=>area.features).Where(f=>f.category=="rail"&&f.kind=="rail"&&!f.tunnel&&f.points?.Length>1&&path.Ways.Contains(f.sourceId)).GroupBy(f=>f.sourceId))
            {
                if(Reserved(group.Key))continue;var feature=group.OrderByDescending(f=>f.points.Length).First();
                if(!string.IsNullOrEmpty(feature.gauge)&&feature.gauge!="1435")continue;
                var run=new List<Vector3>();
                void AddRun()
                {
                    if(PublishedSection(path,region,run.ToArray(),out var points,out var global,out _))
                    {var section=new PublishedChain();section.Points.AddRange(points);section.Global.AddRange(global);section.Ways.Add(feature.sourceId);sections.Add(section);}
                    run.Clear();
                }
                foreach(var point in feature.points){if(!renderer.HasGeometryAt(point)){AddRun();continue;}run.Add(point);}AddRun();
            }
            PublishedChain chain=null;
            foreach(var section in sections.OrderBy(s=>s.Global[0]))
            {
                bool join=chain!=null&&Math.Abs(chain.Global[chain.Global.Count-1]-section.Global[0])<1&&
                    section.Global[1]>chain.Global[chain.Global.Count-1]+.01&&
                    Vector3.Distance(chain.Points[chain.Points.Count-1],section.Points[0])<.6f&&
                    Vector3.Angle(chain.Points[chain.Points.Count-1]-chain.Points[chain.Points.Count-2],section.Points[1]-section.Points[0])<60;
                if(!join){if(chain!=null)yield return chain;chain=new PublishedChain();}
                chain.Points.AddRange(join?section.Points.Skip(1):section.Points);chain.Global.AddRange(join?section.Global.Skip(1):section.Global);chain.Ways.UnionWith(section.Ways);
            }
            if(chain!=null)yield return chain;
        }
        internal string PublishedCoverage(TrainServicePlan plan)=>string.Join("; ",PublishedChains(plan.PublishedPath).Select(c=>c.Global[0].ToString("F0")+".."+c.Global[c.Global.Count-1].ToString("F0")+"m / "+c.Points.Count+" points"));
        bool TryPublishedService(TrainServicePlan plan,TrainServicePosition position)
        {
            if(Vector2.Distance(plan.PublishedPath.DatasetPoint(session.World.Route.Region,session.Car.Body.position),plan.PublishedPath.PointAt(position.Metres))>1200)return false;
            foreach(var candidate in PublishedChains(plan.PublishedPath))
            {
                if(position.Metres<candidate.Global[0]||position.Metres>candidate.Global[candidate.Global.Count-1])continue;
                var points=candidate.Points.ToArray();var global=candidate.Global.ToArray();var local=new double[points.Length];
                for(int i=1;i<points.Length;i++)local[i]=local[i-1]+Vector3.Distance(points[i-1],points[i]);
                double station=ConvertMetres(position.Metres,global,local)+(plan.Direction<0?formation.Length:0);
                var trial=new RailRoute(points.Select(p=>new Vec3(p.x,p.y,p.z)));var pose=formation.Pose(trial,station);
                if(pose.status!="active"||pose.cars.Any(car=>!renderer.HasGeometryAt(RailTrainVisual.ToUnity(car.body.origin)))||!ClearSpawn(pose))continue;
                route=trial;service=plan;travelDirection=plan.Direction;PositionQuality=TrainPositionQuality.Timetable;
                head=publishedHead=station;speed=position.Speed;SourceWay=string.Join(",",candidate.Ways);publishedMap=global;localMap=local;
                visual=new RailTrainVisual(transform,formation);visual.SetPose(pose,true);nextPose=Time.time;Status=formation.Name+" · published timetable estimate";return true;
            }
            return false;
        }
    }
}
