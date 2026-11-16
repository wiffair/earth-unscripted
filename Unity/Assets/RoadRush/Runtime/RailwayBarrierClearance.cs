using UnityEngine;
namespace RoadRush
{
    public static class RailwayBarrierClearance
    {
        public static bool Clear(Vector3 a,Vector3 b,Vector3[] track,float width,float height)
        {
            if(track==null)return true;int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)));
            for(int step=0;step<=steps;step++)
            {
                var p=Vector3.Lerp(a,b,(float)step/steps);
                for(int i=1;i<track.Length;i++)
                {
                    var v=track[i]-track[i-1];var flat=new Vector2(v.x,v.z);float t=Mathf.Clamp01(Vector2.Dot(new Vector2(p.x-track[i-1].x,p.z-track[i-1].z),flat)/Mathf.Max(.0001f,flat.sqrMagnitude));var q=Vector3.Lerp(track[i-1],track[i],t);
                    if(new Vector2(p.x-q.x,p.z-q.z).sqrMagnitude<(width*.5f+.15f)*(width*.5f+.15f)&&p.y+.75f>=q.y-.45f&&p.y-.45f<q.y+height)return false;
                }
            }
            return true;
        }
    }
}
