using UnityEngine;
using RoadRush.Core;
namespace RoadRush
{
    // Opt-in visual adapter, never installed by GameSession. Local metres: +Z car front.
    // Caller owns stationary/in-bay checks, car lock and atomic setup/repair commit.
    public sealed class RacePitCrewCandidate : MonoBehaviour
    {
        public RacePitTransaction Service {get;private set;}=new RacePitTransaction();
        Transform[] mechanics=new Transform[4],newTyres=new Transform[4],oldTyres=new Transform[4],arms=new Transform[4];
        Vector3[] homes=new Vector3[4],stations=new Vector3[4];bool initialized;float propRadius=.36f;
        public void Build(float track=1.64f,float wheelbase=3.6f,float tyreRadius=.36f,float tyreWidth=.35f)
        {
            if(initialized)throw new System.InvalidOperationException("Crew already built");
            foreach(float v in new[]{track,wheelbase,tyreRadius,tyreWidth})if(float.IsNaN(v)||float.IsInfinity(v))throw new System.ArgumentOutOfRangeException();
            if(track<1||track>3||wheelbase<2||wheelbase>5||tyreRadius<.2f||tyreRadius>.6f||tyreWidth<.1f||tyreWidth>.6f)throw new System.ArgumentOutOfRangeException();
            propRadius=tyreRadius;initialized=true;var suit=Geometry.Material("Original crew navy",new Color(.08f,.19f,.30f));var rubber=Geometry.Material("Crew replacement rubber",new Color(.045f,.045f,.05f));var helmet=Geometry.Material("Crew helmet orange",new Color(.95f,.42f,.06f));
            for(int i=0;i<4;i++)
            {
                float side=i<2?-1:1;stations[i]=new Vector3(side*(track*.5f+.55f),0,(i%2==0?-1:1)*wheelbase*.5f);homes[i]=new Vector3(side*4,0,stations[i].z);
                var person=new GameObject("Original mechanic "+i).transform;person.SetParent(transform,false);person.localPosition=homes[i];mechanics[i]=person;
                Geometry.Primitive(PrimitiveType.Capsule,"Overalls",person,new Vector3(0,.95f,0),new Vector3(.42f,.45f,.28f),suit);
                Geometry.Primitive(PrimitiveType.Sphere,"Helmet",person,new Vector3(0,1.6f,0),Vector3.one*.32f,helmet);
                for(int s=-1;s<=1;s+=2)Geometry.Box("Leg",person,new Vector3(s*.12f,.38f,0),new Vector3(.17f,.7f,.2f),suit);
                var arm=new GameObject("Wheel gun arm").transform;arm.SetParent(person,false);arm.localPosition=new Vector3(0,1.15f,0);arms[i]=arm;
                Geometry.Box("Working arms",arm,new Vector3(0,-.15f,.2f),new Vector3(.55f,.16f,.18f),suit);
                Geometry.Box("Wheel gun",arm,new Vector3(.25f,-.15f,.32f),new Vector3(.12f,.12f,.3f),helmet);
                newTyres[i]=Wheel("Replacement tyre",person,rubber,tyreRadius,tyreWidth);oldTyres[i]=Wheel("Removed tyre",person,rubber,tyreRadius,tyreWidth);oldTyres[i].gameObject.SetActive(false);
            }
        }
        // Explicit anchor reconfiguration; never scales actors or moves garage geometry.
        public static Bounds EnvelopeForStations(Vector3[] work,Vector3[] home,float tyreRadius=.36f)
        {
            if(work==null||home==null||work.Length!=4||home.Length!=4||float.IsNaN(tyreRadius)||float.IsInfinity(tyreRadius)||tyreRadius<.2f||tyreRadius>.6f)throw new System.ArgumentException("Four explicit work/home anchors and metric tyre radius required");
            var bounds=new Bounds(work[0],Vector3.zero);
            foreach(var points in new[]{work,home})foreach(var point in points){foreach(float v in new[]{point.x,point.y,point.z})if(float.IsNaN(v)||float.IsInfinity(v)||Mathf.Abs(v)>20)throw new System.ArgumentException("Finite local service anchors within20m required");bounds.Encapsulate(point);}
            float reach=Mathf.Max(.55f,.48f+tyreRadius+.03f);var low=bounds.min-new Vector3(reach,0,reach);var high=bounds.max+new Vector3(reach,1.85f,reach);return new Bounds((low+high)*.5f,high-low);
        }
        public Bounds RequiredLocalBounds {get{if(!initialized)throw new System.InvalidOperationException("Build rig first");return EnvelopeForStations(stations,homes,propRadius);}}
        public static bool Fits(Bounds required,Bounds clearBay)=>clearBay.Contains(required.min)&&clearBay.Contains(required.max);
        public void ConfigureStations(Vector3[] work,Vector3[] home)
        {
            if(!initialized)throw new System.InvalidOperationException("Build rig first");EnvelopeForStations(work,home,propRadius);
            if(Vector3.Distance(transform.lossyScale,Vector3.one)>.0001f)throw new System.InvalidOperationException("Metric actor rig cannot be rescaled to fit");
            System.Array.Copy(work,stations,4);System.Array.Copy(home,homes,4);Pose(RacePitPhase.Idle,0);
        }
        static Transform Wheel(string label,Transform parent,Material material,float radius,float width)
        {var t=Geometry.Primitive(PrimitiveType.Cylinder,label,parent,new Vector3(0,.65f,.48f),new Vector3(radius*2,width*.5f,radius*2),material).transform;t.localRotation=Quaternion.Euler(0,0,90);return t;}
        // PitService calls Pose explicitly, so menu pauses do not advance crew service.
        public void Pose(RacePitPhase phase,float elapsed)
        {
            if(!initialized)return;
            for(int i=0;i<4;i++)
            {
                bool working=phase==RacePitPhase.RemoveWheels||phase==RacePitPhase.FitWheels;
                float approach=phase==RacePitPhase.BringTyres?Mathf.Clamp01(elapsed/1.5f):working?1:phase==RacePitPhase.ReturnOldTyres?1-Mathf.Clamp01((elapsed-4.5f)/1.5f):0;
                mechanics[i].localPosition=Vector3.Lerp(homes[i],stations[i],approach);
                mechanics[i].localRotation=Quaternion.Euler(0,i<2?90:-90,0);
                mechanics[i].localScale=new Vector3(1,working?.72f:1,1);
                arms[i].localRotation=Quaternion.Euler(working?Mathf.Sin(elapsed*18+i)*8:0,0,0);
                newTyres[i].gameObject.SetActive(phase==RacePitPhase.Idle||phase==RacePitPhase.BringTyres||phase==RacePitPhase.RemoveWheels);
                oldTyres[i].gameObject.SetActive(phase==RacePitPhase.FitWheels||phase==RacePitPhase.ReturnOldTyres||phase==RacePitPhase.Complete);
            }
        }
    }
}
