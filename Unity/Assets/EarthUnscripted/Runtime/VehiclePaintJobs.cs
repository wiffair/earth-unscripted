using UnityEngine;

namespace EarthUnscripted
{
    public static class VehiclePaintJobs
    {
        public static readonly string[] Names={"Solid colour","Twin stripes","Rally chevrons","Two tone","Trail splatter"};
        public static Material Create(Material source,Color colour,int pattern,int finish)
        {
            var shader=Resources.Load<Shader>("VehiclePaint");
            var material=shader?GameGraphics.Create(shader):GameGraphics.Create(source);
            material.name="Owned custom paint";material.SetColor("_BaseColor",colour);
            material.SetColor("_AccentColor",colour.grayscale>.55f?new Color(.035f,.04f,.045f):new Color(.92f,.92f,.86f));
            material.SetFloat("_PaintJob",Mathf.Clamp(pattern,0,Names.Length-1));material.SetFloat("_Metallic",finish==0?.55f:.12f);
            material.SetFloat("_Smoothness",finish==1?.16f:finish==2?.94f:.72f);
            return material;
        }
    }
}
