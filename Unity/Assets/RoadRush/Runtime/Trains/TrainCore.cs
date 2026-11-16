using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using RoadRush.Candidates.Compatibility;
namespace RoadRush.Candidates.Trains {
public static class Guard {
 public static void Require(bool ok,string reason){if(!ok)throw new ArgumentException(reason);}
 public static bool Finite(params double[] values){foreach(var x in values)if(double.IsNaN(x)||double.IsInfinity(x))return false;return true;}
}
public readonly struct Point3 {
 public readonly double X,Y,Z;
 public Point3(double x,double y,double z){Guard.Require(Guard.Finite(x,y,z),"nonfinite point");X=x;Y=y;Z=z;}
 public static double Distance(Point3 a,Point3 b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z));
 public static Point3 Lerp(Point3 a,Point3 b,double f)=>new Point3(a.X+(b.X-a.X)*f,a.Y+(b.Y-a.Y)*f,a.Z+(b.Z-a.Z)*f);
}
public sealed class Route {
 readonly Point3[] points;readonly double[] stations;
 public string Digest{get;} public double Length=>stations[stations.Length-1];
 public Route(IEnumerable<Point3> source,string digest){points=new List<Point3>(source).ToArray();Guard.Require(points.Length>=2&&!string.IsNullOrEmpty(digest),"route identity/points");Digest=digest;stations=new double[points.Length];for(int i=1;i<points.Length;i++){var d=Point3.Distance(points[i-1],points[i]);Guard.Require(Guard.Finite(d)&&d>0,"zero/overflow segment");stations[i]=stations[i-1]+d;Guard.Require(Guard.Finite(stations[i]),"route length overflow");}}
 public Point3 At(double station){Guard.Require(Guard.Finite(station),"station");station=Math.Max(0,Math.Min(Length,station));for(int i=1;i<stations.Length;i++)if(station<=stations[i])return Point3.Lerp(points[i-1],points[i],(station-stations[i-1])/(stations[i]-stations[i-1]));return points[points.Length-1];}
}
public enum Quality{Simulated,Interpolated,Measured}
public sealed class Observation {
 public string Service{get;}public string PathDigest{get;}public string Source{get;}public string ClockId{get;}
 public double ObservedUtc{get;}public double ReceivedUtc{get;}public double Distance{get;}public Quality Quality{get;}
 public Observation(string service,string pathDigest,string source,string clockId,double observedUtc,double receivedUtc,double distance,Quality quality){Guard.Require(!string.IsNullOrEmpty(service)&&!string.IsNullOrEmpty(pathDigest)&&!string.IsNullOrEmpty(source)&&!string.IsNullOrEmpty(clockId),"identity");Guard.Require(Guard.Finite(observedUtc,receivedUtc,distance)&&distance>=0&&receivedUtc>=observedUtc&&Enum.IsDefined(typeof(Quality),quality),"observation");Service=service;PathDigest=pathDigest;Source=source;ClockId=clockId;ObservedUtc=observedUtc;ReceivedUtc=receivedUtc;Distance=distance;Quality=quality;}
}
public readonly struct Freshness {
 public readonly double ObservationAge,ReceiptAge,DeliveryDelay;public readonly string Status;
 public Freshness(Observation o,double now,string clock,double maxObservationAge=120,double maxReceiptAge=60,double maxDeliveryDelay=30){Guard.Require(clock==o.ClockId&&Guard.Finite(now,maxObservationAge,maxReceiptAge,maxDeliveryDelay)&&now>=o.ReceivedUtc&&Math.Min(maxObservationAge,Math.Min(maxReceiptAge,maxDeliveryDelay))>0,"clock/freshness");ObservationAge=now-o.ObservedUtc;ReceiptAge=now-o.ReceivedUtc;DeliveryDelay=o.ReceivedUtc-o.ObservedUtc;Status=ObservationAge>maxObservationAge?"stale-observation":ReceiptAge>maxReceiptAge?"stale-receipt":DeliveryDelay>maxDeliveryDelay?"delayed-delivery":"fresh";}
}
public sealed class ReceivedTimeline {
 readonly Observation[] observations;public string ClockId{get;}
 public ReceivedTimeline(Route route,IEnumerable<Observation> input,string clockId,double maxSpeed=55,double maxGap=600){observations=new List<Observation>(input).ToArray();Guard.Require(observations.Length>0&&!string.IsNullOrEmpty(clockId)&&Guard.Finite(maxSpeed,maxGap)&&maxSpeed>0&&maxGap>0,"timeline limits");ClockId=clockId;var first=observations[0];for(int i=0;i<observations.Length;i++){var o=observations[i];Guard.Require(o.ClockId==clockId&&o.PathDigest==route.Digest&&o.Service==first.Service&&o.Source==first.Source&&o.Quality==first.Quality&&o.Distance<=route.Length,"source/clock/path identity");if(i>0){var a=observations[i-1];var dt=o.ObservedUtc-a.ObservedUtc;Guard.Require(dt>0&&dt<=maxGap&&o.ReceivedUtc>a.ReceivedUtc&&Math.Abs(o.Distance-a.Distance)/dt<=maxSpeed,"order/gap/speed");}}}
 public Observation? Latest(double now,string clock,CancellationToken cancel=default){cancel.ThrowIfCancellationRequested();Guard.Require(clock==ClockId&&Guard.Finite(now),"clock");Observation? result=null;foreach(var o in observations){if(o.ReceivedUtc>now)break;result=o;}return result;}
}
public sealed class Consist {
 public int Count{get;}public double CarLength{get;}public double Gap{get;}public double Length=>Count*CarLength+(Count-1)*Gap;
 public Consist(int count=3,double carLength=23,double gap=1){Guard.Require(count>=1&&count<=16&&Guard.Finite(carLength,gap)&&carLength>=5&&carLength<=35&&gap>=0&&gap<=5,"consist");Count=count;CarLength=carLength;Gap=gap;}
}
public sealed class Bounds {
 public string Status{get;}public double Head{get;}public double Tail{get;}public FrozenList<Point3> Centres {get;}
 public Bounds(Route route,double head,int direction,Consist consist){Guard.Require((direction==1||direction==-1)&&Guard.Finite(head),"head/direction");Head=head;Tail=head-direction*consist.Length;bool fits=Math.Min(Head,Tail)>=0&&Math.Max(Head,Tail)<=route.Length;Status=fits?"active":"retirement-needed";var centres=new Point3[fits?consist.Count:0];for(int i=0;i<centres.Length;i++)centres[i]=route.At(head-direction*(consist.CarLength/2+i*(consist.CarLength+consist.Gap)));Centres=FrozenList<Point3>.Own(centres);}
}
public readonly struct Correction {
 public readonly string Status,Reason;public readonly double Distance,Speed;
 public Correction(string status,double distance,double speed,string reason){Status=status;Distance=distance;Speed=speed;Reason=reason;}
}
public static class Policy {
 public static bool RequestAllowed(bool enabled,bool consent,bool permitted,bool secureKeyAvailable,bool host,bool actualUtc,CancellationToken cancel=default)=>!cancel.IsCancellationRequested&&enabled&&consent&&permitted&&secureKeyAvailable&&host&&actualUtc;
 public static bool CrossingClosed(double head,int direction,Consist consist,double crossing,double approach=150,double clearance=10){Guard.Require((direction==1||direction==-1)&&Guard.Finite(head,crossing,approach,clearance)&&approach>=0&&clearance>=0,"crossing");var lead=(head-crossing)*direction;return -approach<=lead&&lead<=consist.Length+clearance;}
 public static Correction Advance(Route route,Consist consist,double head,double speed,int direction,double target,double dt,double maxSpeed=40,double acceleration=1){Guard.Require((direction==1||direction==-1)&&Guard.Finite(head,speed,target,dt,maxSpeed,acceleration)&&dt>0&&maxSpeed>0&&acceleration>0&&speed>=0&&speed<=maxSpeed,"correction limits");if(new Bounds(route,head,direction,consist).Status!="active")return new Correction("retirement-needed",head,speed,"initial extent");var wanted=(target-head)*direction/dt;if(wanted<0||wanted>maxSpeed||Math.Abs(wanted-speed)>acceleration*dt)return new Correction("retirement-needed",head,speed,"forbidden speed/acceleration/reversal");var next=head+direction*wanted*dt;if(new Bounds(route,next,direction,consist).Status!="active")return new Correction("retirement-needed",head,speed,"target extent");return new Correction("active",next,wanted,"bounded step");}
}
public readonly struct Pose {
 public readonly double Distance,SourceUtc,Age;public readonly Quality Quality;public readonly string Status;
 public Pose(double distance,Quality quality,double sourceUtc,double age,string status){Distance=distance;Quality=quality;SourceUtc=sourceUtc;Age=age;Status=status;}
}
public sealed class OfflineTimeline {
 readonly Observation[] observations;readonly double staleAfter;
 public OfflineTimeline(Route route,IEnumerable<Observation> records,string clock,double maxSpeed=55,double maxGap=600,double staleAfter=120){observations=new List<Observation>(records).ToArray();Guard.Require(Guard.Finite(staleAfter)&&staleAfter>0,"stale limit");this.staleAfter=staleAfter;_ = new ReceivedTimeline(route,observations,clock,maxSpeed,maxGap);foreach(var o in observations)Guard.Require(o.Quality==Quality.Simulated,"offline timeline accepts synthetic schedules only");}
 public Pose? Sample(double utc,CancellationToken cancel=default){cancel.ThrowIfCancellationRequested();Guard.Require(Guard.Finite(utc),"clock");if(utc<observations[0].ObservedUtc)return null;foreach(var o in observations)if(utc==o.ObservedUtc)return new Pose(o.Distance,o.Quality,o.ObservedUtc,0,"observed-station");for(int i=1;i<observations.Length;i++){var a=observations[i-1];var b=observations[i];if(utc<b.ObservedUtc){var f=(utc-a.ObservedUtc)/(b.ObservedUtc-a.ObservedUtc);return new Pose(a.Distance+(b.Distance-a.Distance)*f,Quality.Simulated,a.ObservedUtc,utc-a.ObservedUtc,a.Distance==b.Distance?"dwell":"moving");}}var last=observations[observations.Length-1];var age=utc-last.ObservedUtc;return new Pose(last.Distance,Quality.Simulated,last.ObservedUtc,age,age>staleAfter?"stale-hold":"end-hold");}
}

}
