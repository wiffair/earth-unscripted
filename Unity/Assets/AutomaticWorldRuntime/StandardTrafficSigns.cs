using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush.NewbridgePrototype
{
    /// <summary>One master per country/type/legend. Streamed signs clone the
    /// master hierarchy and share its meshes, font atlas and materials.</summary>
    public static class StandardTrafficSigns
    {
        static readonly Dictionary<string,GameObject> templates=new Dictionary<string,GameObject>();
        sealed class Legend { public Mesh mesh; public string words; public Vector2 size; }
        static readonly List<Legend> legends=new List<Legend>();
        static GameObject library;static Font font;static Material black,white;
        const int FontSize=96;static bool rebuildingAtlas;
        const string Alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -./?";
        public static int TemplateCount=>templates.Count;

        static void EnsureLibrary()
        {
            if(library)return;
            library=new GameObject("Standard traffic sign masters");library.hideFlags=HideFlags.HideAndDontSave;
            if(Application.isPlaying)UnityEngine.Object.DontDestroyOnLoad(library);
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            font.RequestCharactersInTexture(Alphabet,FontSize,FontStyle.Bold);
            var shader=Resources.Load<Shader>("RoadRushSignLegend");
            if(!shader)throw new InvalidOperationException("Packaged sign legend shader is missing");
            black=RoadRush.GameGraphics.Create(shader, _rrMaterial => { _rrMaterial.color=Color.black; _rrMaterial.mainTexture=font.material.mainTexture; });
            white=RoadRush.GameGraphics.Create(shader, _rrMaterial => { _rrMaterial.color=Color.white; _rrMaterial.mainTexture=font.material.mainTexture; });
            Font.textureRebuilt+=AtlasChanged;
        }
        static void AtlasChanged(Font changed)
        {
            if(changed!=font||rebuildingAtlas)return;
            rebuildingAtlas=true;
            try
            {
                font.RequestCharactersInTexture(Alphabet,FontSize,FontStyle.Bold);
                black.mainTexture=white.mainTexture=font.material.mainTexture;
                foreach(var legend in legends)if(legend.mesh)BuildLegend(legend);
            }
            finally{rebuildingAtlas=false;}
        }
        static void BuildLegend(Legend legend)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            float y=0;foreach(string line in legend.words.Split('\n'))
            {
                float advance=0;foreach(char letter in line)if(font.GetCharacterInfo(letter,out var g,FontSize,FontStyle.Bold))advance+=g.advance;
                float x=-advance*.5f;
                foreach(char letter in line)
                {
                    if(!font.GetCharacterInfo(letter,out var g,FontSize,FontStyle.Bold))continue;
                    int i=vertices.Count;
                    vertices.Add(new Vector3(x+g.minX,y+g.minY,0));vertices.Add(new Vector3(x+g.minX,y+g.maxY,0));
                    vertices.Add(new Vector3(x+g.maxX,y+g.maxY,0));vertices.Add(new Vector3(x+g.maxX,y+g.minY,0));
                    uv.Add(g.uvBottomLeft);uv.Add(g.uvTopLeft);uv.Add(g.uvTopRight);uv.Add(g.uvBottomRight);
                    // Clockwise as viewed from the sign's +Z front.
                    triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});x+=g.advance;
                }
                y-=FontSize*.95f;
            }
            if(vertices.Count==0)throw new InvalidOperationException("Sign has no supported lettering");
            var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);
            float scale=Mathf.Min(legend.size.x/Mathf.Max(1,bounds.size.x),legend.size.y/Mathf.Max(1,bounds.size.y));
            for(int i=0;i<vertices.Count;i++)vertices[i]=(vertices[i]-bounds.center)*scale;
            legend.mesh.Clear();legend.mesh.SetVertices(vertices);legend.mesh.SetUVs(0,uv);legend.mesh.SetTriangles(triangles,0);legend.mesh.RecalculateBounds();
        }
        static void Text(Transform face,string words,float width,float height,float y=0,bool light=false)
        {
            var legend=new Legend{mesh=new Mesh{name="Shared sign legend "+words.Replace('\n',' ')},words=words,size=new Vector2(width,height)};
            BuildLegend(legend);legends.Add(legend);
            var o=new GameObject("Front legend",typeof(MeshFilter),typeof(MeshRenderer));o.transform.SetParent(face,false);o.transform.localPosition=new Vector3(0,y,.045f);o.transform.localRotation=Quaternion.Euler(0,180,0);
            o.GetComponent<MeshFilter>().sharedMesh=legend.mesh;o.GetComponent<MeshRenderer>().sharedMaterial=light?white:black;
        }
        static void Disc(Transform face,int sides,float radius,Color color,float angle=0)
        {
            TrafficControlVisual.Polygon(face,sides,radius,-.018f,new Color(.28f,.29f,.30f),angle,true);
            TrafficControlVisual.Polygon(face,sides,radius,0,color,angle);
        }
        public static GameObject Place(Transform parent,string kind,string rawValue,string country)
        {
            EnsureLibrary();
            string value=(rawValue??"").Replace(" mph","").Replace(" km/h","").Trim();
            // OSM numeric speeds are bounded; malformed legends never allocate
            // unbounded atlas characters or spill outside the sign face.
            if(kind=="speed"&&value!="national"&&(!int.TryParse(value,out int speed)||speed<1||speed>400))value="?";
            string key=country+"/"+kind+"/"+(kind=="speed"?value:"");
            if(!templates.TryGetValue(key,out var master)||!master)
            {
                master=new GameObject("Standard sign "+key);master.transform.SetParent(library.transform,false);
                switch(kind)
                {
                    case "stop":Disc(master.transform,8,.43f,Color.red,22.5f);Text(master.transform,"STOP",.60f,.235f,0,true);break;
                    case "give_way":
                        Disc(master.transform,3,.5f,Color.red,180);TrafficControlVisual.Polygon(master.transform,3,.39f,.015f,Color.white,180);
                        if(country=="GB")Text(master.transform,"GIVE\nWAY",.31f,.155f,.095f);
                        else if(country=="US")Text(master.transform,"YIELD",.34f,.09f,.105f);break;
                    case "no_entry":Disc(master.transform,32,.4f,Color.red);TrafficControlVisual.Box(master.transform,"Front no-entry bar",new Vector3(0,0,.025f),new Vector3(.60f,.14f,.025f),Color.white);break;
                    case "oneway":
                        TrafficControlVisual.Box(master.transform,"One-way panel",Vector3.zero,new Vector3(.8f,.42f,.04f),country=="US"?Color.white:Color.blue);
                        TrafficControlVisual.Box(master.transform,"Plain sign back",new Vector3(0,0,-.025f),new Vector3(.8f,.42f,.008f),Color.gray);
                        TrafficControlVisual.Arrow(master.transform,country=="US"?Color.black:Color.white);master.transform.localRotation=Quaternion.Euler(0,0,90);break;
                    case "speed":
                        if(value=="national")
                        {Disc(master.transform,32,.4f,Color.white);var slash=TrafficControlVisual.Box(master.transform,"National speed limit",new Vector3(0,0,.025f),new Vector3(.12f,.72f,.025f),Color.black);slash.transform.localRotation=Quaternion.Euler(0,0,-45);break;}
                        if(country=="US"||country=="CA")
                        {
                            TrafficControlVisual.Box(master.transform,"Speed panel",Vector3.zero,new Vector3(.65f,.85f,.04f),Color.white);
                            TrafficControlVisual.Box(master.transform,"Plain sign back",new Vector3(0,0,-.025f),new Vector3(.65f,.85f,.008f),Color.gray);
                            Text(master.transform,country=="CA"?"MAXIMUM":"SPEED\nLIMIT",.50f,.23f,.20f);Text(master.transform,value,.46f,.29f,-.17f);
                        }
                        else{Disc(master.transform,32,.40f,Color.red);TrafficControlVisual.Polygon(master.transform,32,.32f,.015f,Color.white);Text(master.transform,value,.49f,.33f);}break;
                }
                master.SetActive(false);templates[key]=master;
            }
            var clone=UnityEngine.Object.Instantiate(master,parent,false);clone.name=master.name;clone.transform.localPosition=Vector3.up*2.2f;clone.SetActive(true);return clone;
        }
    }
}
