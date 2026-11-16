using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted.Editor
{
    // Appearance-only, bounded GLB2 importer for the reviewed, unbranded source.
    // Coordinate/texture conventions: Khronos glTF2 specification section3.
    // No scripts, external URIs, skins, animations or compressed mesh extensions.
    public static class VehicleAppearanceImporter
    {
        public static string Source=>File.Exists("Data/VehicleAppearance/CarConcept/CarConcept-EarthUnscripted.glb")?Path.GetFullPath("Data/VehicleAppearance/CarConcept/CarConcept-EarthUnscripted.glb"):"/private/tmp/EarthUnscriptedAssetSources/CarConcept/CarConcept-EarthUnscripted.glb";
        public const string SourceSHA256="ebd5b0ee672fac1ff1dc3685f8c7bb045c35350519f1cafae9559ef72325ff98";
        public const string PrefabPath="Assets/EarthUnscripted/Resources/Vehicles/CarConcept.prefab";
        const int MaxFile=64*1024*1024,MaxVertices=2000000,MaxIndices=6000000;
        [Serializable] public sealed class Pivot {public string name;public Vector3 position;public Quaternion rotation;}
        [Serializable] public sealed class Report
        {
            public string sourceHash,assetFolder,coordinateConvention,performancePlan;public long sourceBytes;public int nodes,primitives,vertices,triangles,materials,textures;public Vector3 boundsCenter,boundsSize;public Pivot[] pivots;public string[] fallbacks;
        }
        static void Require(bool value,string message){if(!value)throw new InvalidDataException("Vehicle GLB: "+message);}
        static int Integer(JToken value,int fallback=0){if(value==null)return fallback;Require(value.Type==JTokenType.Integer,"expected integer");long number=(long)value;Require(number>=0&&number<=int.MaxValue,"integer outside bounds");return (int)number;}
        static float Number(JToken value,float fallback=0)
        {if(value==null)return fallback;Require(value.Type==JTokenType.Float||value.Type==JTokenType.Integer,"expected number");double number=(double)value;Require(!double.IsNaN(number)&&!double.IsInfinity(number)&&Math.Abs(number)<=1000000,"non-finite or excessive number");return (float)number;}
        static string Name(JToken value,string fallback){string name=(string)value??fallback;Require(name.Length<=128,"oversized name");return name.Replace('/','-').Replace('\\','-').Replace('\n',' ').Replace('\r',' ');}
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static JArray Array(JToken owner,string key,int maximum)
        {var token=owner[key];Require(token==null||token is JArray,"expected "+key+" array");var values=token as JArray??new JArray();Require(values.Count<=maximum,"too many "+key);return values;}
        static JToken At(JArray values,int index){Require(index>=0&&index<values.Count,"index outside array");return values[index];}
        static Vector3 Mirror(Vector3 p)=>new Vector3(-p.x,p.y,p.z);
        static float[] Floats(JToken token,int count,float[] fallback)
        {if(token==null)return fallback;Require(token is JArray&&token.Count()==count,"vector length mismatch");return token.Select(x=>Number(x)).ToArray();}
        sealed class Document
        {
            public readonly JObject json;public readonly byte[] bin;public readonly int length;public readonly JArray accessors,views,nodes,meshes,materials,images,textures,samplers;
            public Document(byte[] bytes)
            {
                Require(bytes.Length>=28&&bytes.Length<=MaxFile,"file size outside budget");Require(BitConverter.IsLittleEndian,"unsupported big-endian editor");
                Require(BitConverter.ToUInt32(bytes,0)==0x46546c67&&BitConverter.ToUInt32(bytes,4)==2&&BitConverter.ToUInt32(bytes,8)==bytes.Length,"invalid GLB2 header");
                int cursor=12;byte[] text=null,data=null;
                while(cursor<bytes.Length)
                {
                    Require(cursor+8<=bytes.Length,"truncated chunk header");uint size=BitConverter.ToUInt32(bytes,cursor),kind=BitConverter.ToUInt32(bytes,cursor+4);cursor+=8;
                    Require(size%4==0&&size<=MaxFile&&(long)cursor+size<=bytes.Length,"invalid chunk range");
                    if(kind==0x4e4f534a){Require(text==null&&data==null&&size<=8*1024*1024,"JSON chunk order or size");text=new byte[size];Buffer.BlockCopy(bytes,cursor,text,0,(int)size);}
                    else if(kind==0x004e4942){Require(text!=null&&data==null,"BIN chunk order");data=new byte[size];Buffer.BlockCopy(bytes,cursor,data,0,(int)size);}
                    else throw new InvalidDataException("Vehicle GLB: unsupported chunk type");cursor+=(int)size;
                }
                Require(text!=null&&data!=null,"JSON or BIN missing");
                using(var reader=new JsonTextReader(new StringReader(new UTF8Encoding(false,true).GetString(text).TrimEnd(' ','\0'))){MaxDepth=64,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})json=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                Require((string)json["asset"]?["version"]=="2.0","requires glTF2.0");var buffers=Array(json,"buffers",1);Require(buffers.Count==1&&buffers[0]["uri"]==null,"external buffers forbidden");length=Integer(buffers[0]["byteLength"]);Require(length<=data.Length&&data.Length-length<=3,"declared BIN length mismatch");bin=data;
                views=Array(json,"bufferViews",8192);accessors=Array(json,"accessors",4096);nodes=Array(json,"nodes",512);meshes=Array(json,"meshes",512);materials=Array(json,"materials",128);images=Array(json,"images",64);textures=Array(json,"textures",128);samplers=Array(json,"samplers",128);
                Require(Array(json,"skins",0).Count==0&&Array(json,"animations",0).Count==0,"static appearance assets only");
                foreach(var view in views){Require(Integer(view["buffer"])==0,"unknown buffer");long offset=Integer(view["byteOffset"]),size=Integer(view["byteLength"]);Require(offset+size<=length,"bufferView outside BIN");}
                foreach(var extension in Array(json,"extensionsUsed",32))Require((string)extension!="KHR_draco_mesh_compression"&&(string)extension!="EXT_meshopt_compression"&&(string)extension!="KHR_mesh_quantization"&&(string)extension!="EXT_mesh_gpu_instancing","unsupported compressed/instanced geometry");
            }
            public Reader Access(int index,string type)=>new Reader(this,index,type);
            public byte[] Image(int index)
            {
                var image=At(images,index);Require(image["uri"]==null,"external image forbidden");string mime=(string)image["mimeType"];Require(mime=="image/png"||mime=="image/jpeg","unsupported image encoding");var view=At(views,Integer(image["bufferView"]));int count=Integer(view["byteLength"]);Require(count>=16&&count<=16*1024*1024,"image byte budget");var bytes=new byte[count];Buffer.BlockCopy(bin,Integer(view["byteOffset"]),bytes,0,count);ImageSize(bytes,mime,out int width,out int height);Require(width>0&&height>0&&width<=4096&&height<=4096,"image dimension budget");return bytes;
            }
        }
        sealed class Reader
        {
            readonly Document doc;readonly int offset,stride,componentSize;public readonly int count,components,type;public readonly bool normalized;
            public Reader(Document doc,int index,string expected)
            {
                this.doc=doc;var a=At(doc.accessors,index);Require(a["sparse"]==null,"sparse accessors unsupported");Require((string)a["type"]==expected,"accessor type mismatch");components=expected=="SCALAR"?1:expected=="VEC2"?2:expected=="VEC3"?3:expected=="VEC4"?4:0;Require(components>0,"unsupported accessor shape");
                type=Integer(a["componentType"]);componentSize=type==5120||type==5121?1:type==5122||type==5123?2:type==5125||type==5126?4:0;Require(componentSize>0,"unsupported component type");normalized=(bool?)a["normalized"]==true;Require(!normalized||type!=5126&&type!=5125,"invalid normalization");count=Integer(a["count"]);Require(count>0&&count<=(expected=="SCALAR"?MaxIndices:MaxVertices),"accessor count budget");
                var view=At(doc.views,Integer(a["bufferView"]));int relative=Integer(a["byteOffset"]),viewOffset=Integer(view["byteOffset"]),viewSize=Integer(view["byteLength"]);stride=Integer(view["byteStride"],components*componentSize);if(expected=="SCALAR")Require(view["byteStride"]==null,"index buffer byteStride is forbidden");
                Require(stride>=components*componentSize&&stride<=252&&stride%componentSize==0,"invalid byteStride");Require(relative%componentSize==0&&viewOffset%componentSize==0,"unaligned accessor");Require((long)relative+(long)(count-1)*stride+components*componentSize<=viewSize,"accessor outside bufferView");offset=checked(viewOffset+relative);
            }
            public float Value(int item,int component)
            {
                Require(item>=0&&item<count&&component>=0&&component<components,"accessor read outside range");int at=offset+item*stride+component*componentSize;double value;
                switch(type)
                {
                    case 5126:value=BitConverter.ToSingle(doc.bin,at);break;
                    case 5125:value=BitConverter.ToUInt32(doc.bin,at);break;
                    case 5123:value=BitConverter.ToUInt16(doc.bin,at);if(normalized)value/=65535;break;
                    case 5122:value=BitConverter.ToInt16(doc.bin,at);if(normalized)value=Math.Max(-1,value/32767);break;
                    case 5121:value=doc.bin[at];if(normalized)value/=255;break;
                    default:value=(sbyte)doc.bin[at];if(normalized)value=Math.Max(-1,value/127);break;
                }
                Require(!double.IsNaN(value)&&!double.IsInfinity(value)&&Math.Abs(value)<=1000000000,"invalid accessor value");return (float)value;
            }
            public int Index(int item)
            {Require(!normalized&&(type==5121||type==5123||type==5125),"indices must be unsigned integers");int at=offset+item*stride;uint value=type==5121?doc.bin[at]:type==5123?BitConverter.ToUInt16(doc.bin,at):BitConverter.ToUInt32(doc.bin,at);Require(value<int.MaxValue,"oversized vertex index");return (int)value;}
            public Vector3 Vector(int i)=>new Vector3(Value(i,0),Value(i,1),Value(i,2));
        }
        static void ImageSize(byte[] bytes,string mime,out int width,out int height)
        {
            width=height=0;
            if(mime=="image/png")
            {
                Require(bytes.Length>=24&&bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})&&Encoding.ASCII.GetString(bytes,12,4)=="IHDR","invalid PNG header");
                uint Read(int at)=>(uint)bytes[at]<<24|(uint)bytes[at+1]<<16|(uint)bytes[at+2]<<8|bytes[at+3];uint w=Read(16),h=Read(20);Require(w<=4096&&h<=4096,"PNG dimension budget");width=(int)w;height=(int)h;return;
            }
            Require(bytes[0]==255&&bytes[1]==216,"invalid JPEG header");int cursor=2;
            while(cursor+4<bytes.Length)
            {
                Require(bytes[cursor++]==255,"invalid JPEG marker");while(cursor<bytes.Length&&bytes[cursor]==255)cursor++;Require(cursor<bytes.Length,"truncated JPEG marker");int marker=bytes[cursor++];if(marker==217||marker==218)break;if(marker==1||marker>=208&&marker<=215)continue;
                Require(cursor+2<=bytes.Length,"truncated JPEG segment");int size=bytes[cursor]*256+bytes[cursor+1];Require(size>=2&&cursor+size<=bytes.Length,"invalid JPEG segment");
                if(marker==192||marker==193||marker==194){Require(size>=8,"invalid JPEG frame");height=bytes[cursor+3]*256+bytes[cursor+4];width=bytes[cursor+5]*256+bytes[cursor+6];return;}cursor+=size;
            }
            throw new InvalidDataException("Vehicle GLB: JPEG dimensions unavailable");
        }
        sealed class Import
        {
            public readonly Document doc;public readonly Report report=new Report();readonly string folder;readonly List<string> warnings=new List<string>();readonly Dictionary<string,Texture2D> textureCache=new Dictionary<string,Texture2D>();readonly HashSet<int> decoded=new HashSet<int>();long imagePixels;Material[] materials;List<Mesh>[] meshes;Transform[] nodes;int primitiveCount,vertices,indices;
            public Import(Document doc,string folder){this.doc=doc;this.folder=folder;}
            void Warn(string text){if(!warnings.Contains(text))warnings.Add(text);}
            static Vector2 V2(float[] v)=>new Vector2(v[0],v[1]);
            JToken MainInfo(JToken material)=>material["pbrMetallicRoughness"]?["baseColorTexture"]??material["normalTexture"]??material["pbrMetallicRoughness"]?["metallicRoughnessTexture"]??material["emissiveTexture"];
            Vector2 UV(Vector2 uv,JToken info)
            {
                var transform=info?["extensions"]?["KHR_texture_transform"];var scale=Floats(transform?["scale"],2,new[]{1f,1f});var offset=Floats(transform?["offset"],2,new[]{0f,0f});float angle=Number(transform?["rotation"]),c=Mathf.Cos(angle),s=Mathf.Sin(angle);var p=Vector2.Scale(uv,V2(scale));return new Vector2(c*p.x-s*p.y+offset[0],1-(s*p.x+c*p.y+offset[1]));
            }
            bool Compatible(JToken info,JToken main)
            {
                if(info==null)return true;int channel=Integer(info["extensions"]?["KHR_texture_transform"]?["texCoord"],Integer(info["texCoord"]));if(channel!=0){Warn("UV1 occlusion/other maps omitted: builtin URP Lit samples material maps with UV0.");return false;}
                if(!JToken.DeepEquals(info["extensions"]?["KHR_texture_transform"],main?["extensions"]?["KHR_texture_transform"]))
                {Warn("Maps with a texture transform differing from the primary map are omitted; a custom multi-UV shader is a follow-up.");return false;}return true;
            }
            Texture2D Texture(JToken info,string role,float metal=1,float rough=1)
            {
                if(info==null)return null;int index=Integer(info["index"]);var texture=At(doc.textures,index);int image=Integer(texture["source"]),sampler=Integer(texture["sampler"],-1);string key=index+"-"+role+"-"+metal.ToString("R",CultureInfo.InvariantCulture)+"-"+rough.ToString("R",CultureInfo.InvariantCulture);if(textureCache.TryGetValue(key,out var cached))return cached;
                var bytes=doc.Image(image);ImageSize(bytes,(string)At(doc.images,image)["mimeType"],out int width,out int height);if(decoded.Add(image)){imagePixels+=(long)width*height;Require(imagePixels<=32L*1024*1024,"total image pixel budget");}
                string path=folder+"/texture-"+key.Replace('.','_')+".png";var decodedTexture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                try
                {
                    Require(ImageConversion.LoadImage(decodedTexture,bytes,false),"image decoder rejected content");Require(decodedTexture.width==width&&decodedTexture.height==height,"decoded image dimensions changed");
                    if(role=="normal"||role=="metallic")
                    {
                        var pixels=decodedTexture.GetPixels32();for(int i=0;i<pixels.Length;i++)
                        {
                            var pixel=pixels[i];
                            if(role=="normal")pixel.g=(byte)(255-pixel.g); // UV.v flip reverses the tangent-space green axis.
                            else pixel=new Color32((byte)Mathf.RoundToInt(pixel.b*metal),0,0,(byte)Mathf.RoundToInt((1-pixel.g/255f*rough)*255));
                            pixels[i]=pixel;
                        }
                        decodedTexture.SetPixels32(pixels);decodedTexture.Apply(false,false);
                    }
                    File.WriteAllBytes(path,ImageConversion.EncodeToPNG(decodedTexture));
                }
                finally{UnityEngine.Object.DestroyImmediate(decodedTexture);}
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=role=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=role=="colour"||role=="emission";importer.isReadable=false;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.convertToNormalmap=false;importer.flipGreenChannel=false;
                var settings=sampler>=0?At(doc.samplers,sampler):null;TextureWrapMode Wrap(int value)=>value==33071?TextureWrapMode.Clamp:value==33648?TextureWrapMode.Mirror:TextureWrapMode.Repeat;importer.wrapModeU=Wrap(Integer(settings?["wrapS"],10497));importer.wrapModeV=Wrap(Integer(settings?["wrapT"],10497));importer.SaveAndReimport();cached=AssetDatabase.LoadAssetAtPath<Texture2D>(path);textureCache[key]=cached;return cached;
            }
            Material Material(int index)
            {
                var source=At(doc.materials,index);var pbr=source["pbrMetallicRoughness"];float coat=Number(source["extensions"]?["KHR_materials_clearcoat"]?["clearcoatFactor"]);Shader shader=Shader.Find(coat>0?"Universal Render Pipeline/Complex Lit":"Universal Render Pipeline/Lit");Require(shader,"URP shader missing");var material=new Material(shader){name=Name(source["name"],"Material "+index),enableInstancing=true};
                var colour=Floats(pbr?["baseColorFactor"],4,new[]{1f,1f,1f,1f});float metal=Mathf.Clamp01(Number(pbr?["metallicFactor"],1)),rough=Mathf.Clamp01(Number(pbr?["roughnessFactor"],1));var main=MainInfo(source);material.SetColor("_BaseColor",new Color(colour[0],colour[1],colour[2],colour[3]));material.SetFloat("_Metallic",metal);material.SetFloat("_Smoothness",1-rough);material.SetFloat("_Cull",(bool?)source["doubleSided"]==true?0:2);
                if(Compatible(pbr?["baseColorTexture"],main))material.SetTexture("_BaseMap",Texture(pbr?["baseColorTexture"],"colour"));
                if(pbr?["metallicRoughnessTexture"]!=null&&Compatible(pbr["metallicRoughnessTexture"],main))
                {material.SetTexture("_MetallicGlossMap",Texture(pbr["metallicRoughnessTexture"],"metallic",metal,rough));material.SetFloat("_Metallic",1);material.SetFloat("_Smoothness",1);material.EnableKeyword("_METALLICSPECGLOSSMAP");}
                if(source["normalTexture"]!=null&&Compatible(source["normalTexture"],main)){material.SetTexture("_BumpMap",Texture(source["normalTexture"],"normal"));material.SetFloat("_BumpScale",Number(source["normalTexture"]["scale"],1));material.EnableKeyword("_NORMALMAP");}
                if(source["occlusionTexture"]!=null&&Compatible(source["occlusionTexture"],main)){material.SetTexture("_OcclusionMap",Texture(source["occlusionTexture"],"linear"));material.SetFloat("_OcclusionStrength",Number(source["occlusionTexture"]["strength"],1));material.EnableKeyword("_OCCLUSIONMAP");}
                var emission=Floats(source["emissiveFactor"],3,new[]{0f,0f,0f});float strength=Number(source["extensions"]?["KHR_materials_emissive_strength"]?["emissiveStrength"],1);var emissive=new Color(emission[0]*strength,emission[1]*strength,emission[2]*strength,1);material.SetColor("_EmissionColor",emissive);
                if(Compatible(source["emissiveTexture"],main))material.SetTexture("_EmissionMap",Texture(source["emissiveTexture"],"emission"));if(emissive.maxColorComponent>0){material.EnableKeyword("_EMISSION");material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;}
                if(coat>0){material.SetFloat("_ClearCoat",1);material.SetFloat("_ClearCoatMask",Mathf.Clamp01(coat));material.SetFloat("_ClearCoatSmoothness",1-Mathf.Clamp01(Number(source["extensions"]?["KHR_materials_clearcoat"]?["clearcoatRoughnessFactor"])));material.EnableKeyword("_CLEARCOAT");}
                string alpha=(string)source["alphaMode"]??"OPAQUE";float transmission=Number(source["extensions"]?["KHR_materials_transmission"]?["transmissionFactor"]);
                if(alpha=="BLEND"||transmission>0)
                {
                    if(transmission>0){material.SetColor("_BaseColor",new Color(.34f,.45f,.52f,.16f));Warn("Transmission glass approximated with transparent URP Lit; no refractive transmission or thickness.");}
                    material.SetFloat("_Surface",1);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);material.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);material.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);material.SetFloat("_ZWrite",0);material.SetOverrideTag("RenderType","Transparent");material.renderQueue=(int)RenderQueue.Transparent;material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.SetShaderPassEnabled("ShadowCaster",false);
                }
                else if(alpha=="MASK"){material.SetFloat("_AlphaClip",1);material.SetFloat("_Cutoff",Number(source["alphaCutoff"],.5f));material.SetOverrideTag("RenderType","TransparentCutout");material.EnableKeyword("_ALPHATEST_ON");material.renderQueue=(int)RenderQueue.AlphaTest;}
                else Require(alpha=="OPAQUE","unsupported alpha mode");
                if(source["extensions"]?["KHR_materials_iridescence"]!=null)Warn("Iridescence uses the base paint colour/roughness fallback; no thin-film spectral shader.");
                AssetDatabase.CreateAsset(material,folder+"/material-"+index+".mat");material.name=Name(source["name"],"Material "+index);EditorUtility.SetDirty(material);return material;
            }
            Mesh Mesh(JToken primitive,int meshIndex,int primitiveIndex)
            {
                Require(Integer(primitive["mode"],4)==4&&primitive["targets"]==null,"triangle static meshes only");var attributes=primitive["attributes"] as JObject;Require(attributes!=null&&attributes["POSITION"]!=null,"mesh positions missing");var position=doc.Access(Integer(attributes["POSITION"]),"VEC3");Require(position.type==5126,"positions require floats");int count=position.count;vertices+=count;Require(vertices<=MaxVertices&&++primitiveCount<=2048,"total geometry budget");var points=new Vector3[count];for(int i=0;i<count;i++){points[i]=Mirror(position.Vector(i));Require(points[i].sqrMagnitude<1000000,"mesh coordinate outside bounds");}
                int[] triangles;if(primitive["indices"]!=null){var source=doc.Access(Integer(primitive["indices"]),"SCALAR");triangles=new int[source.count];for(int i=0;i<source.count;i++){triangles[i]=source.Index(i);Require(triangles[i]<count,"vertex index outside primitive");}}else triangles=Enumerable.Range(0,count).ToArray();
                Require(triangles.Length%3==0,"incomplete triangle");indices+=triangles.Length;Require(indices<=MaxIndices,"total triangle budget");for(int i=0;i<triangles.Length;i+=3){int swap=triangles[i+1];triangles[i+1]=triangles[i+2];triangles[i+2]=swap;}
                var mesh=new Mesh{name="CarConcept "+meshIndex+"-"+primitiveIndex,indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.vertices=points;mesh.triangles=triangles;
                if(attributes["NORMAL"]!=null){var source=doc.Access(Integer(attributes["NORMAL"]),"VEC3");Require(source.count==count&&source.type==5126,"normal accessor mismatch");var values=new Vector3[count];for(int i=0;i<count;i++){var value=source.Vector(i);Require(value.sqrMagnitude>.25f&&value.sqrMagnitude<4,"invalid normal length");values[i]=Mirror(value).normalized;}mesh.normals=values;}else mesh.RecalculateNormals();
                var originalUV=new Vector2[count];if(attributes["TEXCOORD_0"]!=null){var source=doc.Access(Integer(attributes["TEXCOORD_0"]),"VEC2");Require(source.count==count,"UV count mismatch");for(int i=0;i<count;i++)originalUV[i]=new Vector2(source.Value(i,0),source.Value(i,1));}mesh.uv=originalUV.Select(v=>new Vector2(v.x,1-v.y)).ToArray();
                if(attributes["TEXCOORD_1"]!=null){var source=doc.Access(Integer(attributes["TEXCOORD_1"]),"VEC2");Require(source.count==count,"UV1 count mismatch");var values=new Vector2[count];for(int i=0;i<count;i++)values[i]=new Vector2(source.Value(i,0),1-source.Value(i,1));mesh.uv2=values;}
                if(attributes["TANGENT"]!=null)
                {var source=doc.Access(Integer(attributes["TANGENT"]),"VEC4");Require(source.count==count&&source.type==5126,"tangent accessor mismatch");var values=new Vector4[count];for(int i=0;i<count;i++){var t=Mirror(source.Vector(i)).normalized;float w=source.Value(i,3);Require(Mathf.Abs(Mathf.Abs(w)-1)<.01f,"invalid tangent handedness");values[i]=new Vector4(t.x,t.y,t.z,w);}mesh.tangents=values;}
                else mesh.RecalculateTangents(); // Compute from untransformed UVs, then bake sampling transforms below.
                int material=Integer(primitive["material"]);Require(material<materials.Length,"primitive material missing");var main=MainInfo(At(doc.materials,material));mesh.uv=originalUV.Select(v=>UV(v,main)).ToArray();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"/mesh-"+meshIndex+"-"+primitiveIndex+".asset");return mesh;
            }
            Matrix4x4 Matrix(JToken node)
            {
                Matrix4x4 matrix;
                if(node["matrix"]!=null)
                {var values=Floats(node["matrix"],16,null);matrix=new Matrix4x4();for(int c=0;c<4;c++)for(int r=0;r<4;r++)matrix[r,c]=values[c*4+r];Require(Mathf.Abs(matrix[3,0])+Mathf.Abs(matrix[3,1])+Mathf.Abs(matrix[3,2])+Mathf.Abs(matrix[3,3]-1)<.0001f,"non-affine node matrix");}
                else
                {
                    var p=Floats(node["translation"],3,new[]{0f,0f,0f});var q=Floats(node["rotation"],4,new[]{0f,0f,0f,1f});var scale=Floats(node["scale"],3,new[]{1f,1f,1f});var rotation=new Quaternion(q[0],q[1],q[2],q[3]);Require(Mathf.Abs(Quaternion.Dot(rotation,rotation)-1)<.01f,"invalid quaternion");matrix=Matrix4x4.TRS(new Vector3(p[0],p[1],p[2]),rotation.normalized,new Vector3(scale[0],scale[1],scale[2]));
                }
                var reflection=Matrix4x4.Scale(new Vector3(-1,1,1));return reflection*matrix*reflection;
            }
            void TransformNode(Transform target,JToken node)
            {
                var matrix=Matrix(node);Vector3 x=matrix.GetColumn(0),y=matrix.GetColumn(1),z=matrix.GetColumn(2),p=matrix.GetColumn(3);float sx=x.magnitude,sy=y.magnitude,sz=z.magnitude;Require(sx>.00001f&&sy>.00001f&&sz>.00001f&&sx<1000&&sy<1000&&sz<1000,"degenerate node scale");if(Vector3.Dot(Vector3.Cross(x,y),z)<0)sx=-sx;var scale=new Vector3(sx,sy,sz);var rotation=Quaternion.LookRotation(z/sz,y/sy);var rebuilt=Matrix4x4.TRS(p,rotation,scale);for(int i=0;i<16;i++)Require(Mathf.Abs(rebuilt[i]-matrix[i])<.003f*Mathf.Max(1,Mathf.Abs(matrix[i])),"sheared matrix unsupported");target.localPosition=p;target.localRotation=rotation;target.localScale=scale;
            }
            public GameObject Build()
            {
                foreach(var extension in Array(doc.json,"extensionsUsed",32))if(!new[]{"KHR_materials_clearcoat","KHR_materials_emissive_strength","KHR_materials_iridescence","KHR_materials_transmission","KHR_materials_variants","KHR_texture_transform"}.Contains((string)extension))Warn("Optional extension ignored: "+extension);
                Warn("Material variants retain default primitive materials; runtime paint selection is handled by Earth Unscripted.");
                materials=new Material[doc.materials.Count];Require(materials.Length>0,"materials missing");for(int i=0;i<materials.Length;i++)materials[i]=Material(i);
                var scenes=Array(doc.json,"scenes",32);var scene=At(scenes,Integer(doc.json["scene"]));var roots=Array(scene,"nodes",512);var selected=new HashSet<int>();var parents=new Dictionary<int,int>();
                void Visit(int index,int depth)
                {
                    Require(depth<64&&selected.Add(index),"cycle, duplicate child or repeated scene root");var node=At(doc.nodes,index);foreach(var child in Array(node,"children",512)){int id=Integer(child);Require(!parents.ContainsKey(id),"node has multiple parents");parents[id]=index;Visit(id,depth+1);}
                }
                foreach(var node in roots)Visit(Integer(node),0);
                var used=new HashSet<int>(selected.Where(i=>doc.nodes[i]["mesh"]!=null).Select(i=>Integer(doc.nodes[i]["mesh"])));meshes=new List<Mesh>[doc.meshes.Count];foreach(int m in used){var source=At(doc.meshes,m);var primitives=Array(source,"primitives",64);Require(primitives.Count>0,"empty mesh");meshes[m]=new List<Mesh>();for(int p=0;p<primitives.Count;p++)meshes[m].Add(Mesh(primitives[p],m,p));}
                var root=new GameObject("CarConcept");try
                {
                    nodes=new Transform[doc.nodes.Count];foreach(int i in selected){var node=doc.nodes[i];var transform=new GameObject(Name(node["name"],"Node "+i)).transform;transform.SetParent(root.transform,false);TransformNode(transform,node);nodes[i]=transform;}
                    foreach(var parent in parents)nodes[parent.Key].SetParent(nodes[parent.Value],false);
                    foreach(int i in selected)
                    {
                        var node=doc.nodes[i];if(node["mesh"]==null)continue;int m=Integer(node["mesh"]);var primitives=Array(At(doc.meshes,m),"primitives",64);
                        for(int p=0;p<primitives.Count;p++)
                        {
                            var target=meshes[m].Count==1?nodes[i].gameObject:new GameObject("Primitive "+p);if(target.transform!=nodes[i])target.transform.SetParent(nodes[i],false);target.AddComponent<MeshFilter>().sharedMesh=meshes[m][p];var renderer=target.AddComponent<MeshRenderer>();renderer.sharedMaterial=materials[Integer(primitives[p]["material"])];if(renderer.sharedMaterial.GetFloat("_Surface")>0)renderer.shadowCastingMode=ShadowCastingMode.Off;
                        }
                    }
                    var renderers=root.GetComponentsInChildren<MeshRenderer>();Require(renderers.Length>0,"empty prefab");var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);Require(bounds.size.x>1&&bounds.size.x<10&&bounds.size.y>.5f&&bounds.size.y<5&&bounds.size.z>2&&bounds.size.z<10,"unexpected vehicle bounds");
                    report.nodes=selected.Count;report.primitives=primitiveCount;report.vertices=vertices;report.triangles=indices/3;report.materials=materials.Length;report.textures=textureCache.Count;report.boundsCenter=bounds.center;report.boundsSize=bounds.size;
                    report.pivots=new[]{"WheelFrontL","WheelFrontR","WheelRearL","WheelRearR","InteriorSteeringCylinder"}.Select(name=>{var node=nodes.FirstOrDefault(n=>n&&n.name==name);Require(node,"missing required pivot "+name);return new Pivot{name=name,position=node.position,rotation=node.rotation};}).ToArray();report.fallbacks=warnings.ToArray();return root;
                }
                catch{UnityEngine.Object.DestroyImmediate(root);throw;}
            }
        }
        public static GameObject ImportSanitized(string source)
        {
            Require(new FileInfo(source).Length<=MaxFile,"source file size budget");byte[] bytes=File.ReadAllBytes(source);Require(Hash(bytes)==SourceSHA256,"source is not the reviewed logo-free derivative");var doc=new Document(bytes);string folder="Assets/EarthUnscripted/Art/Vehicles/CarConcept-"+DateTime.UtcNow.Ticks;Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));AssetDatabase.Refresh();GameObject root=null;bool committed=false;
            try
            {
                var import=new Import(doc,folder);root=import.Build();import.report.sourceHash=SourceSHA256;import.report.sourceBytes=bytes.Length;import.report.assetFolder=folder;import.report.coordinateConvention="glTF RH metres to Unity LH metres: mirrorX, S*M*S node transforms, reversed triangle winding, flipped UV.v, reflected normals/tangents, unchanged tangent.w with inverted normal-map green. Original scene scale, offsets, wheel pose and hierarchy retained.";
                import.report.performancePlan="LOD0 retains UV seams and source geometry. No rushed decimation. Follow up: measure draw calls/VRAM, disable hidden internal mechanics at distance, author UV-preserving LODs and merge only compatible static material groups; retain separate animated wheels, brake parts, steering and damage panels.";
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);Require(prefab,"prefab save failed");committed=true;AssetDatabase.SaveAssets();Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/car-concept-import.json",JsonUtility.ToJson(import.report,true));Debug.Log("Car Concept imported: "+import.report.triangles+" triangles, "+import.report.primitives+" primitives, bounds "+import.report.boundsSize+". See Builds/Verification/car-concept-import.json for pivots/fallbacks.");return prefab;
            }
            catch{if(!committed)AssetDatabase.DeleteAsset(folder);throw;}
            finally{if(root)UnityEngine.Object.DestroyImmediate(root);}
        }
        public static int VerifyBoundaries()
        {
            int checks=0;void Check(bool value,string text){Require(value,"verification: "+text);checks++;Debug.Log("VEHICLE IMPORT PASS: "+text);}
            var data=new byte[64];void Float(int at,float value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,data,at,4);
            foreach(var pair in new[]{Tuple.Create(0,1f),Tuple.Create(4,2f),Tuple.Create(8,3f),Tuple.Create(16,4f),Tuple.Create(20,5f),Tuple.Create(24,6f)})Float(pair.Item1,pair.Item2);
            data[32]=1;data[36]=1;data[40]=1;data[48]=1;data[54]=255;data[55]=255;data[56]=0;data[57]=128;
            var json=JObject.Parse(@"{'asset':{'version':'2.0'},'buffers':[{'byteLength':64}],'bufferViews':[{'buffer':0,'byteOffset':0,'byteLength':32,'byteStride':16},{'buffer':0,'byteOffset':32,'byteLength':6},{'buffer':0,'byteOffset':40,'byteLength':12},{'buffer':0,'byteOffset':52,'byteLength':8}],'accessors':[{'bufferView':0,'componentType':5126,'type':'VEC3','count':2},{'bufferView':1,'componentType':5123,'type':'SCALAR','count':3},{'bufferView':2,'componentType':5125,'type':'SCALAR','count':3},{'bufferView':3,'componentType':5123,'normalized':true,'type':'VEC2','count':2}]}");
            byte[] GLB(JObject source)
            {
                var raw=Encoding.UTF8.GetBytes(source.ToString(Formatting.None));int padded=(raw.Length+3)&~3;using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
                {writer.Write(0x46546c67u);writer.Write(2u);writer.Write((uint)(12+8+padded+8+data.Length));writer.Write((uint)padded);writer.Write(0x4e4f534au);writer.Write(raw);for(int i=raw.Length;i<padded;i++)writer.Write((byte)32);writer.Write((uint)data.Length);writer.Write(0x004e4942u);writer.Write(data);return stream.ToArray();}
            }
            var doc=new Document(GLB(json));var positions=doc.Access(0,"VEC3");Check(positions.Vector(0)==new Vector3(1,2,3)&&positions.Vector(1)==new Vector3(4,5,6),"interleaved float accessor obeys byteStride");
            Check(doc.Access(1,"SCALAR").Index(0)==1&&doc.Access(1,"SCALAR").Index(1)==0&&doc.Access(2,"SCALAR").Index(2)==1,"u16 and u32 indices decode without float truncation");
            Check(doc.Access(3,"VEC2").Value(0,1)==1&&Mathf.Abs(doc.Access(3,"VEC2").Value(1,0)-32768/65535f)<.00001f,"normalized u16 UVs retain their range");
            bool Fails(Action action){try{action();return false;}catch(InvalidDataException){return true;}}
            var invalid=(JObject)json.DeepClone();invalid["accessors"][0]["byteOffset"]=16;Check(Fails(()=>new Document(GLB(invalid)).Access(0,"VEC3")),"out-of-range strided accessors are rejected");
            invalid=(JObject)json.DeepClone();invalid["bufferViews"][0]["byteStride"]=10;Check(Fails(()=>new Document(GLB(invalid)).Access(0,"VEC3")),"unaligned undersized strides are rejected");
            invalid=(JObject)json.DeepClone();invalid["accessors"][0]["sparse"]=new JObject();Check(Fails(()=>new Document(GLB(invalid)).Access(0,"VEC3")),"sparse accessors fail explicitly rather than silently losing data");
            var broken=GLB(json);broken[8]=0;Check(Fails(()=>new Document(broken)),"GLB declared length must match its actual bytes");
            Vector3 n=Vector3.forward,t=Vector3.right,b=Vector3.Cross(n,t);var expected=Mirror(t*.3f+b*.4f+n*Mathf.Sqrt(.75f));var imported=Mirror(t)*.3f+Vector3.Cross(Mirror(n),Mirror(t))*(-.4f)+Mirror(n)*Mathf.Sqrt(.75f);
            Check(Vector3.Distance(expected,imported)<.00001f,"reflected tangent basis plus green inversion reproduces the original mapped normal after UV.v flip");
            var a=Vector3.zero;var c=Vector3.up;Check(Vector3.Dot(Vector3.Cross(Mirror(c)-a,Mirror(Vector3.right)-a),Mirror(Vector3.forward))>.99f,"reversed mirrored triangle winding agrees with transformed normals");
            Check(File.Exists(Source)&&Hash(File.ReadAllBytes(Source))==SourceSHA256,"only the reviewed unbranded source passes its pinned digest");
            Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/vehicle-import-boundaries.txt","PASS "+checks+" GLB accessor / bounds / tangent-space / source checks\n");return checks;
        }
        public static void RepairGeneratedMaterialNames()
        {
            var report=JsonUtility.FromJson<Report>(File.ReadAllText("Builds/Verification/car-concept-import.json"));Require(report.assetFolder.StartsWith("Assets/EarthUnscripted/Art/Vehicles/CarConcept-",StringComparison.Ordinal),"unexpected generated asset folder");var doc=new Document(File.ReadAllBytes(Source));
            for(int i=0;i<doc.materials.Count;i++)
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(report.assetFolder+"/material-"+i+".mat");Require(material,"generated material missing");material.name=Name(doc.materials[i]["name"],"Material "+i);EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();Debug.Log("Restored source semantic names on "+doc.materials.Count+" generated materials, preserving asset GUIDs.");
        }
        public static int VerifyImportedPacking()
        {
            var report=JsonUtility.FromJson<Report>(File.ReadAllText("Builds/Verification/car-concept-import.json"));Require(report.assetFolder.StartsWith("Assets/EarthUnscripted/Art/Vehicles/CarConcept-",StringComparison.Ordinal),"unexpected generated asset folder");var doc=new Document(File.ReadAllBytes(Source));int checks=0;
            void Check(bool value,string text){Require(value,"asset verification: "+text);checks++;Debug.Log("VEHICLE ASSET PASS: "+text);}
            Texture2D Decode(byte[] bytes){var t=new Texture2D(2,2,TextureFormat.RGBA32,false,true);if(!ImageConversion.LoadImage(t,bytes,false)){UnityEngine.Object.DestroyImmediate(t);throw new InvalidDataException("Verification image decode failed");}return t;}
            var checkedNormals=new HashSet<string>();
            for(int i=0;i<doc.materials.Count;i++)
            {
                var source=doc.materials[i];var material=AssetDatabase.LoadAssetAtPath<Material>(report.assetFolder+"/material-"+i+".mat");Check(material&&material.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal)&&material.name==Name(source["name"],"Material "+i),"material "+i+" retains its source semantic name and actual URP shader");
                var info=source["pbrMetallicRoughness"]?["metallicRoughnessTexture"];
                if(info!=null&&material.GetTexture("_MetallicGlossMap"))
                {
                    int image=Integer(At(doc.textures,Integer(info["index"]))["source"]);var original=Decode(doc.Image(image));var packed=Decode(File.ReadAllBytes(AssetDatabase.GetAssetPath(material.GetTexture("_MetallicGlossMap"))));
                    try
                    {
                        var a=original.GetPixels32();var b=packed.GetPixels32();float metal=Number(source["pbrMetallicRoughness"]?["metallicFactor"],1),rough=Number(source["pbrMetallicRoughness"]?["roughnessFactor"],1);bool equal=a.Length==b.Length;
                        foreach(int sample in new[]{0,a.Length/3,a.Length/2,a.Length-1})equal=equal&&Math.Abs(b[sample].r-Mathf.RoundToInt(a[sample].b*metal))<=1&&Math.Abs(b[sample].a-Mathf.RoundToInt((1-a[sample].g/255f*rough)*255))<=1&&b[sample].g==0&&b[sample].b==0;
                        Check(equal&&material.GetFloat("_Metallic")==1&&material.GetFloat("_Smoothness")==1,"actual metallic map packs glTF B to Unity R and factored inverse G to smoothness A");
                        var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetTexture("_MetallicGlossMap")));Check(!importer.sRGBTexture,"metallic and smoothness texture is sampled linearly");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(packed);}
                }
                info=source["normalTexture"];
                if(info!=null&&material.GetTexture("_BumpMap"))
                {
                    string path=AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap"));if(!checkedNormals.Add(path))continue;int image=Integer(At(doc.textures,Integer(info["index"]))["source"]);var original=Decode(doc.Image(image));var packed=Decode(File.ReadAllBytes(path));
                    try
                    {
                        var a=original.GetPixels32();var b=packed.GetPixels32();bool equal=a.Length==b.Length;foreach(int sample in new[]{0,a.Length/3,a.Length/2,a.Length-1})equal=equal&&b[sample].r==a[sample].r&&b[sample].g==255-a[sample].g&&b[sample].b==a[sample].b;
                        var importer=(TextureImporter)AssetImporter.GetAtPath(path);Check(equal&&importer.textureType==TextureImporterType.NormalMap&&!importer.sRGBTexture&&!importer.flipGreenChannel,"actual normal PNG "+image+" has one green inversion and a linear Unity normal importer");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(packed);}
                }
            }
            for(int m=0;m<doc.meshes.Count;m++)
            {
                var primitives=Array(doc.meshes[m],"primitives",64);for(int p=0;p<primitives.Count;p++)
                {
                    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(report.assetFolder+"/mesh-"+m+"-"+p+".asset");if(!mesh)continue;var primitive=primitives[p];var attributes=primitive["attributes"];var positions=doc.Access(Integer(attributes["POSITION"]),"VEC3");
                    Check(mesh.isReadable&&Vector3.Distance(mesh.vertices[0],Mirror(positions.Vector(0)))<.000001f,"mesh "+m+"-"+p+" retains readable reflected source vertices for damage");
                    var triangles=mesh.triangles;var input=primitive["indices"]!=null?doc.Access(Integer(primitive["indices"]),"SCALAR"):null;Check(triangles[0]==(input?.Index(0)??0)&&triangles[1]==(input?.Index(2)??2)&&triangles[2]==(input?.Index(1)??1),"mesh "+m+"-"+p+" stores reversed glTF triangle winding");
                }
            }
            return checks;
        }
        [MenuItem("Earth Unscripted/Import reviewed Car Concept appearance")]
        public static void Run()
        {try{VerifyBoundaries();ImportSanitized(Source);if(Application.isBatchMode)EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);else throw;}}
    }
}
