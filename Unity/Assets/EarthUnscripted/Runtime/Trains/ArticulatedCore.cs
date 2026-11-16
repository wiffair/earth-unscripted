using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EarthUnscripted.Candidates.Compatibility;
namespace EarthUnscripted.Candidates.Articulated {
public static class Vec {
 public static Vec3 Add(Vec3 a,Vec3 b)=>new Vec3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
 public static Vec3 Sub(Vec3 a,Vec3 b)=>new Vec3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
 public static Vec3 Mul(Vec3 a,double s)=>new Vec3(a.X*s,a.Y*s,a.Z*s);
 public static double Norm(Vec3 a)=>Math.Sqrt(a.X*a.X+a.Y*a.Y+a.Z*a.Z);
 public static double Dot(Vec3 a,Vec3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
 public static Vec3 Cross(Vec3 a,Vec3 b)=>new Vec3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
 public static Vec3 Unit(Vec3 a){var n=Norm(a);if(n<=1e-12||!double.IsFinite(n))throw new ArgumentException("degenerate frame");return Mul(a,1/n);}
}
public readonly struct Frame {
 public Vec3 origin{get;}public Vec3 right{get;}public Vec3 up{get;}public Vec3 forward{get;}
 public Frame(Vec3 origin,Vec3 right,Vec3 up,Vec3 forward){this.origin=origin;this.right=right;this.up=up;this.forward=forward;}
 public static Frame From(Vec3 origin,Vec3 forward){var right=Vec.Unit(Vec.Cross(new Vec3(0,1,0),forward));return new Frame(origin,right,Vec.Unit(Vec.Cross(forward,right)),forward);}
 public Vec3 Local(Vec3 p)=>Vec.Add(origin,Vec.Add(Vec.Mul(right,p.X),Vec.Add(Vec.Mul(up,p.Y),Vec.Mul(forward,p.Z))));
}
public sealed class RailRoute {
 readonly Vec3[] points;readonly double[] stations;public double Length=>stations[stations.Length-1];
 public RailRoute(IEnumerable<Vec3> input){points=input.ToArray();if(points.Length<2||points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z)))throw new ArgumentException("points");stations=new double[points.Length];for(int i=1;i<points.Length;i++){var d=Vec.Norm(Vec.Sub(points[i],points[i-1]));if(!double.IsFinite(d)||d<=0)throw new ArgumentException("segment");stations[i]=stations[i-1]+d;if(!double.IsFinite(stations[i]))throw new ArgumentException("length");}}
 public RailRoute(IEnumerable<double[]> input):this(input.Select(p=>(Vec3)p)){}
 public Vec3 At(double s){if(!double.IsFinite(s)||s<0||s>Length)throw new ArgumentException("unsupported station; no clamp");if(s==Length)return points[points.Length-1];int index=Array.BinarySearch(stations,s);if(index<0)index=~index-1;return Vec.Add(points[index],Vec.Mul(Vec.Sub(points[index+1],points[index]),(s-stations[index])/(stations[index+1]-stations[index])));}
 public Frame AtFrame(double s,double halfbase,int direction){if(halfbase<=0||!double.IsFinite(halfbase)||(direction!=1&&direction!=-1))throw new ArgumentException("frame");return Frame.From(At(s),Vec.Unit(Vec.Sub(At(s+direction*halfbase),At(s-direction*halfbase))));}
}
public sealed class Wheel {public Vec3 contact{get;}public Vec3 centre{get;}public double phase{get;}public Wheel(Vec3 contact,Vec3 centre,double phase){this.contact=contact;this.centre=centre;this.phase=phase;}}
public sealed class Axle {public double station{get;}public Frame frame{get;}public FrozenList<Wheel> wheels{get;}public Axle(double station,Frame frame,FrozenList<Wheel> wheels){this.station=station;this.frame=frame;this.wheels=wheels??throw new ArgumentNullException(nameof(wheels));}}
public sealed class Bogie {public double station{get;}public Frame frame{get;}public FrozenList<Axle> axles{get;}public Bogie(double station,Frame frame,FrozenList<Axle> axles){this.station=station;this.frame=frame;this.axles=axles??throw new ArgumentNullException(nameof(axles));}}
public sealed class Envelope {public Vec3 min{get;}public Vec3 max{get;}public Envelope(Vec3 min,Vec3 max){this.min=min;this.max=max;}public static Envelope From(IEnumerable<Vec3> input){var first=true;double x0=0,y0=0,z0=0,x1=0,y1=0,z1=0;foreach(var p in input){if(first){x0=x1=p.X;y0=y1=p.Y;z0=z1=p.Z;first=false;}else{x0=Math.Min(x0,p.X);y0=Math.Min(y0,p.Y);z0=Math.Min(z0,p.Z);x1=Math.Max(x1,p.X);y1=Math.Max(y1,p.Y);z1=Math.Max(z1,p.Z);}}if(first)throw new ArgumentException("empty envelope");return new Envelope(new Vec3(x0,y0,z0),new Vec3(x1,y1,z1));}}
public sealed class Car {public double station{get;}public Frame body{get;}public FrozenList<Bogie> bogies{get;}public FrozenList<Vec3> couplers{get;}public Envelope envelope{get;}public Car(double station,Frame body,FrozenList<Bogie> bogies,FrozenList<Vec3> couplers,Envelope envelope){this.station=station;this.body=body;this.bogies=bogies??throw new ArgumentNullException(nameof(bogies));this.couplers=couplers??throw new ArgumentNullException(nameof(couplers));this.envelope=envelope;}}
public sealed class Pose {public string status{get;}public double head{get;}public double tail{get;}public FrozenList<Car> cars{get;}public FrozenList<double>? couplerGaps{get;}public Envelope? envelope{get;}public Pose(string status,double head,double tail,FrozenList<Car> cars,FrozenList<double>? couplerGaps=null,Envelope? envelope=null){this.status=status;this.head=head;this.tail=tail;this.cars=cars??throw new ArgumentNullException(nameof(cars));this.couplerGaps=couplerGaps;this.envelope=envelope;}}
public static class Articulation {
 public static Pose Build(RailRoute route,double head,int count,int direction,CancellationToken cancellation=default){cancellation.ThrowIfCancellationRequested();if((count!=2&&count!=3&&count!=4)||(direction!=1&&direction!=-1)||!double.IsFinite(head))throw new ArgumentException("pose arguments");var tail=head-direction*(count*23+(count-1));var low=Math.Min(head,tail)-.2;var high=Math.Max(head,tail)+.2;if(low<0||high>route.Length)return new Pose("retirement-needed",head,tail,FrozenList<Car>.Own(Array.Empty<Car>()));
 var cars=new Car[count];for(int i=0;i<count;i++){cancellation.ThrowIfCancellationRequested();var station=head-direction*(11.5+i*24);var b0=route.At(station-direction*8);var b1=route.At(station+direction*8);var body=Frame.From(Vec.Mul(Vec.Add(b0,b1),.5),Vec.Unit(Vec.Sub(b1,b0)));var bogies=new Bogie[2];for(int bi=0;bi<2;bi++){double z=bi==0?-8:8;var bs=station+direction*z;var bf=route.AtFrame(bs,1.25,direction);var axles=new Axle[2];for(int ai=0;ai<2;ai++){double az=ai==0?-1.25:1.25;var ws=bs+direction*az;var wf=route.AtFrame(ws,.25,direction);var wheels=new Wheel[2];for(int wi=0;wi<2;wi++){double x=wi==0?-.7175:.7175;wheels[wi]=new Wheel(wf.Local(new Vec3(x,0,0)),wf.Local(new Vec3(x,.42,0)),direction*ws/.42);}axles[ai]=new Axle(ws,wf,FrozenList<Wheel>.Own(wheels));}bogies[bi]=new Bogie(bs,bf,FrozenList<Axle>.Own(axles));}
 var corners=new Vec3[8+8*8];int corner=0;for(int xi=0;xi<2;xi++)for(int yi=0;yi<2;yi++)for(int zi=0;zi<2;zi++)corners[corner++]=body.Local(new Vec3(xi==0?-1.5:1.5,yi==0?.2:4.05,zi==0?-11.7:11.7));foreach(var bogie in bogies)foreach(var axle in bogie.axles)foreach(var wheel in axle.wheels)for(int xi=0;xi<2;xi++)for(int yi=0;yi<2;yi++)for(int zi=0;zi<2;zi++)corners[corner++]=Vec.Add(wheel.centre,Vec.Add(Vec.Mul(axle.frame.right,xi==0?-.10:.10),Vec.Add(Vec.Mul(axle.frame.up,yi==0?-.44:.44),Vec.Mul(axle.frame.forward,zi==0?-.44:.44))));cars[i]=new Car(station,body,FrozenList<Bogie>.Own(bogies),FrozenList<Vec3>.Own(new[]{body.Local(new Vec3(0,.8,-11.7)),body.Local(new Vec3(0,.8,11.7))}),Envelope.From(corners));}
 var gaps=new double[count-1];for(int i=1;i<count;i++)gaps[i-1]=Vec.Norm(Vec.Sub(cars[i-1].couplers[0],cars[i].couplers[1]));var all=new Vec3[count*2];for(int i=0;i<count;i++){all[i*2]=cars[i].envelope.min;all[i*2+1]=cars[i].envelope.max;}return new Pose("active",head,tail,FrozenList<Car>.Own(cars),FrozenList<double>.Own(gaps),Envelope.From(all));}
}
}
