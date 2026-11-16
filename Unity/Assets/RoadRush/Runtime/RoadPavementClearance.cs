using System;using System.IO;using System.Linq;using UnityEngine;
namespace RoadRush
{
    // Preparation-only exact final pavement/corner clearance; existing capsules
    // remain a conservative local broadphase. Guests use pinned accepted trees.
    public sealed class RoadPavementClearance
    {
        readonly FinalizedRoadSurfacePlan pavement;readonly (int layer,bool bridge)[] levels;
        public RoadPavementClearance(GeoRegion owner,FinalizedRoadSurfacePlan prepared)
        {
            if(prepared==null||prepared.Owner!=owner||prepared.WidthField!=owner.PreparedRoadWidths)throw new InvalidDataException("Canopy requires matching final road pavement descriptors");pavement=prepared;levels=owner.roads.Where(r=>!r.tunnel).Select(r=>(r.layer,r.bridge)).Distinct().OrderBy(x=>x.layer).ThenBy(x=>x.bridge).ToArray();
        }
        static float Distance(Vector2 p,Vector2 a,Vector2 b){var d=b-a;return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.000001f,d.sqrMagnitude)));}
        public bool Allows(float x,float z,float margin)
        {
            if(float.IsNaN(margin)||float.IsInfinity(margin)||margin<0||margin>100)return false;var p=new Vector2(x,z);var query=new[]{new Vector3(x-margin,0,z-margin),new Vector3(x+margin,0,z-margin),new Vector3(x+margin,0,z+margin)};
            foreach(var level in levels)foreach(var f in pavement.Pavement.At(query,level.layer,level.bridge))
            {
                bool inside=false;var ring=f.Polygon;for(int i=0,j=ring.Length-1;i<ring.Length;j=i++){var a=ring[i];var b=ring[j];if(Distance(p,a,b)<margin)return false;if((a.y>z)!=(b.y>z)&&x<(double)(b.x-a.x)*(z-a.y)/(b.y-a.y)+a.x)inside=!inside;}if(inside)return false;
            }
            return true;
        }
    }
}
