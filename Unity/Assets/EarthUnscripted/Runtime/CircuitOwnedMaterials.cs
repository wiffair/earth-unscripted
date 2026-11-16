using System;
using UnityEngine;
namespace EarthUnscripted
{
    // One material instance per palette entry/per scenery owner. No Geometry cache lookup or registration.
    public static class CircuitOwnedMaterials
    {
        public static Material Create(string name,Color colour,float metal=0,float smooth=.25f)
        {
            bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
            if(string.IsNullOrWhiteSpace(name)||name.Length>160||!Finite(colour.r)||!Finite(colour.g)||!Finite(colour.b)||!Finite(colour.a)||colour.r<0||colour.r>1||colour.g<0||colour.g>1||colour.b<0||colour.b>1||colour.a<0||colour.a>1||!Finite(metal)||!Finite(smooth)||metal<0||metal>1||smooth<0||smooth>1)throw new ArgumentException("Invalid owned circuit palette material");
            var library=SurfaceLibrary.Instance;var template=library?library.Surface:null;Material material;
            if(template)material=GameGraphics.Create(template);
            else {var shader=Shader.Find("Universal Render Pipeline/Lit");if(!shader)throw new InvalidOperationException("Circuit owned surface shader missing");material=GameGraphics.Create(shader);}
            material.name=name;material.color=colour;material.SetFloat("_Metallic",metal);material.SetFloat("_Smoothness",smooth);material.enableInstancing=true;return material;
        }
    }
}
