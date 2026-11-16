using System;using System.Collections.Generic;using System.IO;
namespace EarthUnscripted
{
 // Immutable evaluator for an already validated ordered continuation path.
 // Registry/codec proves joins and topology; this class never discovers a path.
 public sealed class RoadElevationArmPath
 {
  public readonly struct Span
  {public readonly string RoadId;public readonly double From,To,RawStart,RawEnd,HorizontalStart,HorizontalEnd;public readonly int Direction;internal readonly RoadElevationCurve Curve;internal Span(string id,double from,double to,double raw,double horizontal,RoadElevationCurve curve){RoadId=id;From=from;To=to;Direction=to>from?1:-1;RawStart=raw;RawEnd=raw+Math.Abs(to-from);HorizontalStart=horizontal;HorizontalEnd=horizontal+Math.Abs(curve.Metric.ToHorizontal(to)-curve.Metric.ToHorizontal(from));Curve=curve;}}
  readonly Span[] spans;readonly Dictionary<string,int> byRoad=new Dictionary<string,int>(StringComparer.Ordinal);
  public int SpanCount=>spans.Length;public double RawLength{get;}public double HorizontalLength{get;}public double CoreHorizontal{get;}public string FirstRoadId=>spans[0].RoadId;
  public RoadElevationArmPath(RoadJunctionArmDto arm,IReadOnlyDictionary<string,RoadElevationCurve> curves)
  {
   if(arm==null||arm.spans==null||arm.spans.Length<1||arm.spans.Length>64||curves==null||!Finite(arm.sourceDistance)||!Finite(arm.coreLength)||!Finite(arm.collarLength)||arm.coreLength<0||arm.collarLength<=arm.coreLength||arm.collarLength>500)throw new InvalidDataException("Elevation path shape");spans=new Span[arm.spans.Length];double raw=0,horizontal=0;
   for(int i=0;i<spans.Length;i++){var value=arm.spans[i];if(value==null||value.roadId==null||!curves.TryGetValue(value.roadId,out var curve)||byRoad.ContainsKey(value.roadId)||!Finite(value.fromSourceDistance)||!Finite(value.toSourceDistance)||Math.Abs(value.toSourceDistance-value.fromSourceDistance)<1e-8||value.fromSourceDistance<0||value.toSourceDistance<0||value.fromSourceDistance>curve.Metric.SourceLength+1e-8||value.toSourceDistance>curve.Metric.SourceLength+1e-8)throw new InvalidDataException("Elevation path source span");var span=new Span(value.roadId,value.fromSourceDistance,value.toSourceDistance,raw,horizontal,curve);if(i==0&&(span.RoadId!=arm.roadId||Math.Abs(span.From-arm.sourceDistance)>1e-8||span.Direction!=arm.direction))throw new InvalidDataException("Elevation path origin");spans[i]=span;byRoad.Add(span.RoadId,i);raw=span.RawEnd;horizontal=span.HorizontalEnd;}
   if(Math.Abs(raw-arm.collarLength)>1e-8||horizontal<.0001)throw new InvalidDataException("Elevation path total length");RawLength=raw;HorizontalLength=horizontal;CoreHorizontal=HorizontalAtRaw(arm.coreLength);if(HorizontalLength-CoreHorizontal<.0001)throw new InvalidDataException("Elevation path collar span");
  }
  static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
  public Span GetSpan(int index)=>spans[index];public RoadElevationCurve GetCurve(string roadId){if(roadId==null||!byRoad.TryGetValue(roadId,out int i))throw new InvalidDataException("Foreign elevation path road");return spans[i].Curve;}
  public bool TryLocate(string roadId,double sourceS,out double outwardH,out int direction)
  {outwardH=0;direction=0;if(!Finite(sourceS)||roadId==null||!byRoad.TryGetValue(roadId,out int i))return false;var span=spans[i];double raw=span.Direction*(sourceS-span.From);if(raw< -1e-8||raw>span.RawEnd-span.RawStart+1e-8)return false;sourceS=Math.Max(Math.Min(span.From,span.To),Math.Min(Math.Max(span.From,span.To),sourceS));outwardH=span.HorizontalStart+Math.Abs(span.Curve.Metric.ToHorizontal(sourceS)-span.Curve.Metric.ToHorizontal(span.From));direction=span.Direction;return true;}
  public void SourceAtRawOffset(double offset,out string roadId,out double sourceS)
  {if(!Finite(offset)||offset< -1e-8||offset>RawLength+1e-8)throw new ArgumentOutOfRangeException(nameof(offset));offset=Math.Max(0,Math.Min(RawLength,offset));int i=0;while(i<spans.Length-1&&offset>spans[i].RawEnd)i++;var span=spans[i];roadId=span.RoadId;sourceS=span.From+span.Direction*(offset-span.RawStart);}
  public double HorizontalAtRaw(double offset){SourceAtRawOffset(offset,out var id,out double s);var span=spans[byRoad[id]];return span.HorizontalStart+Math.Abs(span.Curve.Metric.ToHorizontal(s)-span.Curve.Metric.ToHorizontal(span.From));}
 }
}
