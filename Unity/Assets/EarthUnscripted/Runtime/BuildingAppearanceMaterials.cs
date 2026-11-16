using UnityEngine;

namespace EarthUnscripted
{
    // Shared reviewed CC0 surfaces; fictional colour choices are stable by building ID.
    public static class BuildingAppearanceMaterials
    {
        static readonly Material[] walls=new Material[6],doors=new Material[4];
        static Material roof,frame,glass,access;
        public static uint Seed(string id){unchecked{uint value=2166136261;foreach(char c in id??"")value=(value^c)*16777619;return value;}}
        public static int WallIndex(GeoBuilding building)=>building.kind=="important_building"?3:(int)(Seed(building.id)%6);
        public static float WallRepeat(GeoBuilding building){int index=WallIndex(building);return index<2?1.4f:index==5?2.57f:1;}
        public static Material Wall(GeoBuilding building)
        {
            int i=WallIndex(building);if(walls[i])return walls[i];
            Color[] tints={new Color(.9f,.83f,.77f),new Color(.68f,.64f,.61f),new Color(.96f,.91f,.8f),new Color(.8f,.78f,.73f),new Color(.97f,.97f,.94f),new Color(.8f,.83f,.85f)};
            return walls[i]=Surface("Terrace wall "+i,i<2?"Brick":i==5?"Stone":"Render",tints[i]);
        }
        public static Material Roof=>roof?roof:roof=Surface("Metric slate roofing","Slate",new Color(.72f,.76f,.82f));
        public static Material Frame=>frame?frame:frame=Geometry.Material("Original painted window frames",new Color(.81f,.83f,.8f),0,.35f);
        public static Material Glass=>glass?glass:glass=Geometry.Material("Opaque reflective window panes",new Color(.075f,.12f,.145f),.18f,.83f);
        public static Material Access=>access?access:access=Surface("Supported garden stair concrete","Render",new Color(.68f,.7f,.7f));
        public static Material Door(int palette)
        {
            if(palette<0||palette>=doors.Length)throw new System.ArgumentOutOfRangeException(nameof(palette));
            if(doors[palette])return doors[palette];
            Color[] colours={new Color(.075f,.16f,.11f),new Color(.07f,.12f,.2f),new Color(.25f,.065f,.045f),new Color(.13f,.14f,.145f)};
            return doors[palette]=Geometry.Material("Original painted entrance "+palette,colours[palette],0,.4f);
        }
        static Material Surface(string name,string source,Color tint)
        {
            var material=Geometry.Material(name,tint);var colour=Resources.Load<Texture2D>("BuildingAppearance/"+source+"Colour");
            if(colour){material.mainTexture=colour;material.mainTextureScale=Vector2.one;}
            var normal=Resources.Load<Texture2D>("BuildingAppearance/"+source+"Normal");
            if(normal){material.SetTexture("_BumpMap",normal);material.SetFloat("_BumpScale",.65f);material.EnableKeyword("_NORMALMAP");}
            var packed=Resources.Load<Texture2D>("BuildingAppearance/"+source+"MetalSmoothness");
            if(packed){material.SetTexture("_MetallicGlossMap",packed);material.SetFloat("_Smoothness",.75f);material.EnableKeyword("_METALLICSPECGLOSSMAP");}
            return material;
        }
    }
}
