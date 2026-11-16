using System;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace RoadRush
{
    [Serializable] public sealed class RoadSyntheticAnchorDto
    {
        public string id,kind,parentRoadId,connectorRoadId,sourcePin;
        public double parentSourceDistance,connectorSourceDistance;
        public const int MaximumAnchors=1024;
        public static void Validate(IReadOnlyList<RoadSyntheticAnchorDto> anchors,IReadOnlyList<GeoRoad> roads,IReadOnlyList<RoadElevationProfileDto> profiles,string sourceDigest)
        {
            if(anchors==null||anchors.Count>MaximumAnchors)throw new InvalidDataException("Synthetic anchor count");var byId=roads.ToDictionary(r=>r.id,StringComparer.Ordinal);var controls=profiles.ToDictionary(p=>p.roadId,StringComparer.Ordinal);var ids=new HashSet<string>(StringComparer.Ordinal);var connectors=new HashSet<string>(StringComparer.Ordinal);var connectorEnds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var a in anchors)
            {
                if(a==null||!RoadSourceIdentity.Identifier(a.id)||!ids.Add(a.id)||(a.kind!="service"&&a.kind!="pit")||a.parentRoadId==a.connectorRoadId||!byId.TryGetValue(a.parentRoadId??"",out var parent)||!byId.TryGetValue(a.connectorRoadId??"",out var connector)||!RoadElevationProfileDto.Finite(a.parentSourceDistance)||!RoadElevationProfileDto.Finite(a.connectorSourceDistance)||!connectorEnds.Add(a.connectorRoadId+"|"+a.connectorSourceDistance.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))throw new InvalidDataException("Synthetic anchor identity/relation");
                connectors.Add(a.connectorRoadId);var pm=new RoadElevationMetric(parent);var cm=new RoadElevationMetric(connector);
                if(a.parentSourceDistance<0||a.parentSourceDistance>pm.SourceLength+1e-8||a.connectorSourceDistance<0||a.connectorSourceDistance>cm.SourceLength+1e-8||Math.Min(Math.Abs(a.connectorSourceDistance),Math.Abs(a.connectorSourceDistance-cm.SourceLength))>1e-8)throw new InvalidDataException("Synthetic anchor station/end binding");
                var p=SourcePoint(parent,a.parentSourceDistance);var q=SourcePoint(connector,a.connectorSourceDistance);if((new Vector2(p.x,p.z)-new Vector2(q.x,q.z)).sqrMagnitude>.0004)throw new InvalidDataException("Synthetic anchor source XZ mismatch");
                double py=Elevation(controls[parent.id],pm,a.parentSourceDistance),cy=Elevation(controls[connector.id],cm,a.connectorSourceDistance);if(Math.Abs(py-cy)>.001)throw new InvalidDataException("Synthetic anchor derived height mismatch");
                if(a.sourcePin!=RoadSourceIdentity.AnchorPin(a,sourceDigest))throw new InvalidDataException("Synthetic anchor source commitment mismatch");
            }
            foreach(var road in roads)if((road.id=="fictional-service-approach"||road.id=="fictional-pit-lane")&&!connectors.Contains(road.id))throw new InvalidDataException("Cached synthetic connector lacks explicit source relation; reprepare source");
        }
        internal static Vector3 SourcePoint(GeoRoad road,double source)
        {
            double start=0;for(int i=1;i<road.points.Length;i++){var a=road.points[i-1];var b=road.points[i];double dx=(double)b.x-a.x,dy=(double)b.y-a.y,dz=(double)b.z-a.z,length=Math.Sqrt(dx*dx+dy*dy+dz*dz);if(source<=start+length||i==road.points.Length-1)return Vector3.Lerp(a,b,(float)Math.Max(0,Math.Min(1,(source-start)/length)));start+=length;}return road.points[road.points.Length-1];
        }
        internal static double Elevation(RoadElevationProfileDto p,RoadElevationMetric metric,double source)
        {
            int i=0;while(i<p.distances.Length-2&&source>p.distances[i+1])i++;double lo=metric.ToHorizontal(p.distances[i]),hi=metric.ToHorizontal(p.distances[i+1]),t=Math.Max(0,Math.Min(1,(metric.ToHorizontal(source)-lo)/(hi-lo)));return RoadElevationProfileDto.Eval(RoadElevationProfileDto.CentrePolynomial(p,i,metric),t);
        }
    }
}
