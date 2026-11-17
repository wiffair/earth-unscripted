using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    public static class Geometry
    {
        static Material fontMaterial;static Font fontSource;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        public static Material Material(string name, Color colour, float metal = 0, float smooth = .25f)
        {
            if (materials.TryGetValue(name, out var found) && found) return found;
            var library = SurfaceLibrary.Instance;
            var m = library && library.Surface ? GameGraphics.Create(library.Surface) : GameGraphics.Create(Shader.Find("Universal Render Pipeline/Lit")); m.name = name;
            m.color = colour; m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); m.enableInstancing = true;
            materials[name] = m; return m;
        }
        public static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision = false)
            => Primitive(PrimitiveType.Cube, name, parent, position, scale, material, collision);
        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision = false)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(parent);

            var g = GameObject.CreatePrimitive(type); g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = position; g.transform.localScale = scale; g.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) { var c = g.GetComponent<Collider>(); c.enabled = false; if(Application.isPlaying)Object.Destroy(c);else Object.DestroyImmediate(c); }
            return g;
        }
        public static GameObject Mesh(string name, Transform parent, Vector3[] vertices, int[] triangles, Vector2[] uv, Material material, bool collision)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(parent);

            using var telemetry=WorkTelemetry.Measure("mesh-upload-and-collider",name,.25);using var bootMesh=BootTelemetry.Measure("main.mesh.total",name);
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            if(collision)VehicleObstacleWheels.MarkSuspensionSurface(g,name);
            Mesh m;using(BootTelemetry.Measure("main.mesh.upload",name))m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = vertices, triangles = triangles, uv = uv };
            using(BootTelemetry.Measure("main.mesh.normals-bounds",name))using(WorkTelemetry.Measure("mesh-normals-and-bounds",name,.25)){m.RecalculateNormals();m.RecalculateBounds();}
            g.AddComponent<MeshFilter>().sharedMesh = m;g.AddComponent<GeneratedMeshOwner>().Mesh=m;
            g.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collision)using(BootTelemetry.Measure("main.mesh.collider-cook",name))using(WorkTelemetry.Measure("mesh-collider-cook",name,.25))g.AddComponent<MeshCollider>().sharedMesh = m;
            return g;
        }
        public static void ReleaseMeshes(GameObject root)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(root ? root.transform : null);
if(root)foreach(var owner in root.GetComponentsInChildren<GeneratedMeshOwner>(true))owner.Release();}
        public static Material Surface(string name, Color colour, float noise, float repeat)
        {
            var m = Material(name, colour);
            if(ArtSurfaces.Apply(name,m))return m;
            if (m.mainTexture) return m;
            const int size = 128; var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name + " grain", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
            var pixels = new Color[size * size]; var random = new System.Random(12);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white * (1 - noise + (float)random.NextDouble() * noise);
            texture.SetPixels(pixels); texture.Apply(); m.mainTexture = texture; m.mainTextureScale = Vector2.one * repeat; return m;
        }
        public static TextMesh Label(string text, Transform parent, Vector3 position, Quaternion rotation, float size = .8f, Color? colour = null)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(parent);

            var g = new GameObject(text); g.transform.SetParent(parent, false); g.transform.localPosition = position; g.transform.localRotation = rotation;
            var t = g.AddComponent<TextMesh>(); t.text = text; t.characterSize = size; t.fontSize = 48; t.anchor = TextAnchor.MiddleCenter; t.color = colour ?? Color.white;
            if(SurfaceLibrary.Instance && SurfaceLibrary.Instance.SignFont){t.font=SurfaceLibrary.Instance.SignFont;g.GetComponent<MeshRenderer>().sharedMaterial=WorldFont(t.font);}
            g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }
        static Material WorldFont(Font font)
        {
            var shader=SurfaceLibrary.Instance?SurfaceLibrary.Instance.WorldText:null;if(!shader)return font.material;
            if(!fontMaterial){fontSource=font;fontMaterial=GameGraphics.Create(shader, _euMaterial => { _euMaterial.name="Depth-tested world lettering"; });Font.textureRebuilt+=RefreshFont;}
            fontMaterial.mainTexture=font.material.mainTexture;return fontMaterial;
        }
        static void RefreshFont(Font font){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceFontCallback();
if(fontMaterial&&font==fontSource)fontMaterial.mainTexture=font.material.mainTexture;}
        public static void BoardText(string text,Transform parent,Vector3 centre,float width,float height,float thickness,Color? colour=null)
        {
            FittedLabel(text,parent,centre+Vector3.back*(thickness*.5f+.018f),Quaternion.identity,width,height,colour);
            FittedLabel(text,parent,centre+Vector3.forward*(thickness*.5f+.018f),Quaternion.Euler(0,180,0),width,height,colour);
        }
        public static TextMesh FittedLabel(string text,Transform parent,Vector3 position,Quaternion rotation,float width,float height,Color? colour=null)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(parent);

            var label=Label(text,parent,position,rotation,.1f,colour);
            if(label.font)label.font.RequestCharactersInTexture(text,label.fontSize,label.fontStyle);
            var bounds=label.GetComponent<MeshRenderer>().localBounds;
            if(bounds.size.x>.001f&&bounds.size.y>.001f)label.transform.localScale=Vector3.one*Mathf.Min(width/bounds.size.x,height/bounds.size.y);
            label.gameObject.AddComponent<FitWorldText>().Initialize(width,height);
            return label;
        }
    }
}
