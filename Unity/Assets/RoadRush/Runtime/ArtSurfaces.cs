using UnityEngine;
namespace RoadRush
{
    public static class ArtSurfaces
    {
        public static bool Apply(string name,Material material)
        {
            if(!name.ToLowerInvariant().Contains("asphalt"))return false;var colour=Resources.Load<Texture2D>("Textures/AsphaltColour");if(!colour)return false;material.mainTexture=colour;material.color=new Color(.75f,.75f,.75f);material.mainTextureScale=Vector2.one*4;
            var normal=Resources.Load<Texture2D>("Textures/AsphaltNormal");if(normal){material.SetTexture("_BumpMap",normal);material.SetFloat("_BumpScale",.5f);material.EnableKeyword("_NORMALMAP");}return true;
        }
        public static Material Terrain(Texture2D land)
        {
            var shader=SurfaceLibrary.Instance?SurfaceLibrary.Instance.Terrain:null;if(!shader)return Geometry.Material("Geographic land cover",Color.white);
            var material=GameGraphics.Create(shader, _rrMaterial => { _rrMaterial.name="Detailed geographic ground"; });material.SetTexture("_BaseMap",land);foreach(string key in new[]{"Grass","Rock"}){material.SetTexture("_"+key,Resources.Load<Texture2D>("Textures/"+key+"Colour"));material.SetTexture("_"+key+"Normal",Resources.Load<Texture2D>("Textures/"+key+"Normal"));}return material;
        }
    }
}
