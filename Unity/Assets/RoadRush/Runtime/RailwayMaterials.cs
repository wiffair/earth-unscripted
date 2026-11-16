using UnityEngine;
namespace RoadRush
{
    // Shared world-metric surfaces. Reuses already reviewed local CC0 maps; no downloads.
    public static class RailwayMaterials
    {
        static Texture2D sleeperGrain;
        static readonly System.Collections.Generic.HashSet<Material> initialized=new System.Collections.Generic.HashSet<Material>();
        public static Material Rail(bool disused)=>Geometry.Material(disused?"Railway rusted steel":"Railway running steel",disused?new Color(.25f,.13f,.075f):new Color(.34f,.37f,.39f),disused?.55f:.85f,disused?.17f:.48f);
        public static Material Sleepers
        {
            get
            {
                var m=Geometry.Material("Railway sleepers",new Color(.23f,.20f,.16f),0,.12f);if(initialized.Contains(m))return m;
                // Original subtle weathered grain, not bark or a claimed photographed sleeper.
                if(!sleeperGrain)
                {
                    const int size=128;sleeperGrain=new Texture2D(size,size,TextureFormat.RGBA32,true){name="Original railway timber grain",wrapMode=TextureWrapMode.Repeat,anisoLevel=4};var colours=new Color[size*size];
                    var random=new System.Random(71023);var bands=new float[size];for(int x=0;x<size;x++)bands[x]=(float)random.NextDouble();
                    for(int z=0;z<size;z++)for(int x=0;x<size;x++){float grain=.78f+bands[x]*.14f+(float)random.NextDouble()*.08f;colours[z*size+x]=new Color(grain,grain,grain,1);}sleeperGrain.SetPixels(colours);sleeperGrain.Apply(false,true);
                }
                m.mainTexture=sleeperGrain;m.mainTextureScale=new Vector2(3,.7f);initialized.Add(m);return m;
            }
        }
        // Boulder source reused at a deliberately reduced art scale for coarse formation texture.
        // It is an approximation to ballast; no dedicated surveyed/gravel material is claimed.
        public static Material Ballast(bool abandoned)=>Surface(abandoned?"Railway abandoned formation":"Railway ballast","Textures/Rock",abandoned?new Color(.62f,.65f,.49f):new Color(.76f,.75f,.72f),2.7f,.35f,null,.10f);
        public static Material Earth=>Surface("Railway approach earth","Textures/Grass",new Color(.59f,.51f,.34f),.20f,.4f,null,.08f);
        // Source-review.json records rock_wall_03 as a 2.57m tile; keep courses at that scale.
        public static Material Structure=>Surface("Railway bridge masonry","BuildingAppearance/Stone",new Color(.73f,.72f,.69f),1/2.57f,.65f,"BuildingAppearance/StoneMetalSmoothness",.70f);
        public static Material Barrier=>Geometry.Material("Railway bridge safety steel",new Color(.23f,.25f,.25f),.65f,.25f);
        static Material Surface(string name,string resource,Color tint,float repeat,float normalScale,string packed,float smoothness)
        {
            var m=Geometry.Material(name,tint,0,smoothness);if(initialized.Contains(m))return m;
            var colour=Resources.Load<Texture2D>(resource+"Colour");if(colour){m.mainTexture=colour;m.mainTextureScale=Vector2.one*repeat;}
            var normal=Resources.Load<Texture2D>(resource+"Normal");if(normal){m.SetTexture("_BumpMap",normal);m.SetFloat("_BumpScale",normalScale);m.EnableKeyword("_NORMALMAP");}
            if(packed!=null){var map=Resources.Load<Texture2D>(packed);if(map){m.SetTexture("_MetallicGlossMap",map);m.EnableKeyword("_METALLICSPECGLOSSMAP");}}
            initialized.Add(m);return m;
        }
    }
}
