using UnityEngine;
namespace EarthUnscripted
{
 public static class TrafficJunctionRules
 {
  public static int Priority(GeoRoad road)
  {
   if(road.roundabout)return 100;
   switch(road.kind){case "motorway":case "motorway_link":return 8;case "trunk":case "trunk_link":return 7;case "primary":case "primary_link":return 6;case "secondary":case "secondary_link":return 5;case "tertiary":case "tertiary_link":return 4;case "residential":case "unclassified":return 3;default:return 2;}
  }
  public static bool MustYield(GeoRoad own,GeoRoad other,float ownETA,float otherETA,float distance,float heightGap,bool approaching,bool otherOnRight)
  {
   if(own==null||other==null||own.id==other.id||heightGap>3||!approaching)return false;
   if(otherETA>6||otherETA>ownETA+2.5f)return false;
   int a=Priority(own),b=Priority(other);
   if(own.roundabout&&!other.roundabout)return false;
   return b>a||(b==a&&otherOnRight)||distance<7;
  }
 }
}
