using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    /// <summary>One master per country/type/legend. Streamed signs clone the
    /// master hierarchy and share its meshes, font atlas and materials.</summary>
    public static class StandardTrafficSigns
    {
        static readonly Dictionary<string,GameObject> templates=new Dictionary<string,GameObject>();
        sealed class Legend { public Mesh mesh; public string words; public Vector2 size; }
        static readonly List<Legend> legends=new List<Legend>();
        static GameObject library;static Material black,white;
        static readonly Dictionary<char,CharacterInfo> glyphs=new Dictionary<char,CharacterInfo>();
        static Texture2D letteringAtlas;
        const int FontSize=96;
        const string Alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -./?";
        public static int TemplateCount=>templates.Count;

        static void EnsureLibrary()
        {
            if(library)return;
            library=new GameObject("Standard traffic sign masters");library.hideFlags=HideFlags.HideAndDontSave;
            if(Application.isPlaying)UnityEngine.Object.DontDestroyOnLoad(library);
        }
        static void EnsureLettering()
        {
            if(letteringAtlas)return;
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            font.RequestCharactersInTexture(Alphabet,FontSize,FontStyle.Bold);
            foreach(char letter in Alphabet)if(font.GetCharacterInfo(letter,out var glyph,FontSize,FontStyle.Bold))glyphs[letter]=glyph;
            // A dynamic UI/TextMesh font can repack its atlas during a drive.
            // Keep the standard sign masters on their own immutable atlas/UVs.
            var source=(Texture2D)font.material.mainTexture;
            letteringAtlas=new Texture2D(source.width,source.height,source.format,false){name="Immutable traffic sign lettering",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            letteringAtlas.Apply(false,true);
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)Graphics.CopyTexture(source,letteringAtlas);
            var shader=Resources.Load<Shader>("EarthUnscriptedSignLegend");
            if(!shader)throw new InvalidOperationException("Packaged sign legend shader is missing");
            black=EarthUnscripted.GameGraphics.Create(shader, _euMaterial => { _euMaterial.color=Color.black; _euMaterial.mainTexture=letteringAtlas; });
            white=EarthUnscripted.GameGraphics.Create(shader, _euMaterial => { _euMaterial.color=Color.white; _euMaterial.mainTexture=letteringAtlas; });
        }
        static void BuildLegend(Legend legend)
        {
            if(legend.words.All(char.IsDigit)){BuildNumbers(legend);return;}
            EnsureLettering();
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            float y=0;foreach(string line in legend.words.Split('\n'))
            {
                float advance=0;foreach(char letter in line)if(glyphs.TryGetValue(letter,out var g))advance+=g.advance;
                float x=-advance*.5f;
                foreach(char letter in line)
                {
                    if(!glyphs.TryGetValue(letter,out var g))continue;
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
            legend.mesh.Clear();legend.mesh.SetVertices(vertices);legend.mesh.SetUVs(0,uv);legend.mesh.SetTriangles(triangles,0);legend.mesh.RecalculateBounds();legend.mesh.RecalculateNormals();
        }
        // Speed numerals are solid vector strokes. They have no glyph texture
        // or font-atlas binding to become stale during scenery streaming.
        static void BuildNumbers(Legend legend)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            void Triangle(Vector2 a,Vector2 b,Vector2 c){int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.AddRange(new[]{n,n+1,n+2});}
            for(int digit=0;digit<legend.words.Length;digit++)foreach(var line in NumberStrokes(legend.words[digit]))
            {
                var points=line.Select(p=>p+Vector2.right*(digit*.8f)).ToArray();
                for(int n=1;n<points.Length;n++){var a=points[n-1];var b=points[n];var d=(b-a).normalized;var side=new Vector2(-d.y,d.x)*.055f;Triangle(a-side,a+side,b+side);Triangle(a-side,b+side,b-side);}
                foreach(var p in points)for(int n=0;n<12;n++){float a=n*Mathf.PI/6,b=(n+1)*Mathf.PI/6;Triangle(p,p+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*.055f,p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.055f);}
            }
            var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var p in vertices)bounds.Encapsulate(p);float scale=Mathf.Min(legend.size.x/bounds.size.x,legend.size.y/bounds.size.y);
            for(int n=0;n<vertices.Count;n++)vertices[n]=(vertices[n]-bounds.center)*scale;
            legend.mesh.Clear();legend.mesh.SetVertices(vertices);legend.mesh.SetTriangles(triangles,0);legend.mesh.RecalculateNormals();legend.mesh.RecalculateBounds();
        }
        static List<List<Vector2>> NumberStrokes(char digit)
        {
            var result=new List<List<Vector2>>();List<Vector2> line=null;
            void Move(float x,float y){line=new List<Vector2>{new Vector2(x,y)};result.Add(line);}
            void To(float x,float y)=>line.Add(new Vector2(x,y));
            void Curve(float x1,float y1,float x2,float y2,float x,float y){var a=line[line.Count-1];var b=new Vector2(x1,y1);var c=new Vector2(x2,y2);var d=new Vector2(x,y);for(int n=1;n<=12;n++){float t=n/12f,u=1-t;line.Add(u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d);}}
            void Oval(float x,float y,float rx,float ry){Move(x+rx,y);for(int n=1;n<=32;n++){float a=n*Mathf.PI/16;To(x+rx*Mathf.Cos(a),y+ry*Mathf.Sin(a));}}
            switch(digit)
            {
                case '0':Oval(.34f,.5f,.27f,.45f);break;
                case '1':Move(.15f,.8f);To(.37f,.97f);To(.37f,.04f);Move(.14f,.04f);To(.6f,.04f);break;
                case '2':Move(.06f,.82f);Curve(.12f,1.1f,.7f,1.09f,.62f,.77f);Curve(.57f,.55f,.15f,.34f,.06f,.05f);To(.65f,.05f);break;
                case '3':Move(.07f,.94f);Curve(.65f,1.15f,.85f,.55f,.33f,.52f);Curve(.86f,.6f,.75f,-.14f,.06f,.08f);break;
                case '4':Move(.5f,.03f);To(.5f,.97f);To(.02f,.3f);To(.65f,.3f);break;
                case '5':Move(.63f,.96f);To(.12f,.96f);To(.08f,.55f);Curve(.79f,.77f,.85f,-.16f,.05f,.08f);break;
                case '6':Move(.59f,.96f);Curve(-.03f,1.1f,-.14f,-.12f,.42f,.05f);Curve(.9f,.2f,.59f,.75f,.08f,.52f);break;
                case '7':Move(.03f,.96f);To(.65f,.96f);To(.2f,.03f);break;
                case '8':Oval(.34f,.27f,.27f,.23f);Oval(.34f,.73f,.24f,.23f);break;
                case '9':return NumberStrokes('6').Select(path=>path.Select(p=>new Vector2(.68f-p.x,1-p.y)).ToList()).ToList();
            }
            return result;
        }
        static void Text(Transform face,string words,float width,float height,float y=0,bool light=false)
        {
            var legend=new Legend{mesh=new Mesh{name="Shared sign legend "+words.Replace('\n',' ')},words=words,size=new Vector2(width,height)};
            BuildLegend(legend);legends.Add(legend);
            var o=new GameObject("Front legend",typeof(MeshFilter),typeof(MeshRenderer));o.transform.SetParent(face,false);o.transform.localPosition=new Vector3(0,y,.045f);o.transform.localRotation=Quaternion.Euler(0,180,0);
            o.GetComponent<MeshFilter>().sharedMesh=legend.mesh;o.GetComponent<MeshRenderer>().sharedMaterial=words.All(char.IsDigit)?TrafficControlVisual.Mat(light?Color.white:Color.black):light?white:black;
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
                        else{Disc(master.transform,32,.40f,Color.red);TrafficControlVisual.Polygon(master.transform,32,.32f,.015f,Color.white);Text(master.transform,value,.53f,.39f);}break;
                }
                master.SetActive(false);templates[key]=master;
            }
            var clone=UnityEngine.Object.Instantiate(master,parent,false);clone.name=master.name;clone.transform.localPosition=Vector3.up*2.2f;clone.SetActive(true);return clone;
        }
    }
}
