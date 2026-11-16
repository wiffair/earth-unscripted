using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted.Editor
{
    // Asset preparation only. Runtime instancing, materials and wind belong to the
    // presentation layer; no physics, world snapshot or source LiDAR is imported.
    public static class BroadleafAssetImporter
    {
        const string Source="Data/Vegetation/Compiled",Destination="Assets/EarthUnscripted/Resources/Vegetation";
        static int checks;
        static void Check(bool value,string description){if(!value)throw new InvalidDataException("BROADLEAF FAIL: "+description);checks++;Debug.Log("BROADLEAF PASS: "+description);}
        static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        static float Finite(BinaryReader r){float v=r.ReadSingle();if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidDataException("Non-finite vegetation vertex");return v;}
        static Mesh Read(string path)
        {
            using(var input=File.OpenRead(path))using(var reader=new BinaryReader(input))
            {
                if(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))!="EUBM2\0\0\0")throw new InvalidDataException("Invalid broadleaf mesh header");
                uint vc=reader.ReadUInt32(),ic=reader.ReadUInt32();if(vc<3||vc>100000||ic<3||ic>300000||ic%3!=0||input.Length!=16L+vc*60L+ic*4L)throw new InvalidDataException("Invalid broadleaf mesh budget");
                var vertices=new Vector3[vc];var normals=new Vector3[vc];var uv=new Vector2[vc];var colours=new Color[vc];var pivots=new List<Vector3>((int)vc);
                for(int i=0;i<vc;i++)
                {
                    vertices[i]=new Vector3(Finite(reader),Finite(reader),Finite(reader));normals[i]=new Vector3(Finite(reader),Finite(reader),Finite(reader));uv[i]=new Vector2(Finite(reader),Finite(reader));colours[i]=new Color(Finite(reader),Finite(reader),Finite(reader),Finite(reader));pivots.Add(new Vector3(Finite(reader),Finite(reader),Finite(reader)));
                    if(vertices[i].y<-.00001f||vertices[i].y>1.00001f||new Vector2(vertices[i].x,vertices[i].z).magnitude>.94001f||Mathf.Abs(normals[i].magnitude-1)>.001f)throw new InvalidDataException("Broadleaf bounds or normals violate contract");
                    if(colours[i].r<0||colours[i].r>1||colours[i].g<0||colours[i].g>1||colours[i].b<0||colours[i].b>1||colours[i].a!=1)throw new InvalidDataException("Broadleaf colour channels violate contract");
                }
                var indices=new int[ic];for(int i=0;i<ic;i++){uint value=reader.ReadUInt32();if(value>=vc)throw new InvalidDataException("Broadleaf index outside vertex array");indices[i]=(int)value;}
                var mesh=new Mesh{name=Path.GetFileNameWithoutExtension(path),indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.colors=colours;mesh.SetUVs(1,pivots);mesh.triangles=indices;mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
            }
        }
        static void Texture(string name,bool leaves)
        {
            string destination=Destination+"/"+name+".png";File.Copy(Source+"/"+name+".png",destination,true);AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(destination);importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=leaves;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=leaves;importer.alphaTestReferenceValue=.4f;importer.wrapMode=leaves?TextureWrapMode.Clamp:TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.maxTextureSize=1024;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.isReadable=false;importer.SaveAndReimport();
        }
        static void DetailTexture(string name,bool normal)
        {
            string destination=Destination+"/"+name+".png";File.Copy(Source+"/"+name+".png",destination,true);AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(destination);importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=false;importer.alphaSource=TextureImporterAlphaSource.None;importer.alphaIsTransparency=false;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=false;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.maxTextureSize=1024;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.convertToNormalmap=false;importer.flipGreenChannel=false;importer.isReadable=true;importer.SaveAndReimport();
        }
        static void VerifyDetailTextures(JObject meta)
        {
            foreach(string name in new[]{"LeafNormal","LeafRoughness"})
            {
                var item=((JArray)meta["textures"]).Single(t=>(string)t["file"]==name+".png");Check(Hash(Source+"/"+name+".png")== (string)item["sha256"],"detail source hash "+name);
                Check(Hash(Destination+"/"+name+".png")== (string)item["sha256"],"imported source PNG is exact prepared "+name);
            }
            var normalImporter=(TextureImporter)AssetImporter.GetAtPath(Destination+"/LeafNormal.png");var roughImporter=(TextureImporter)AssetImporter.GetAtPath(Destination+"/LeafRoughness.png");
            Check(normalImporter.textureType==TextureImporterType.NormalMap&&!normalImporter.sRGBTexture&&!normalImporter.convertToNormalmap&&!normalImporter.flipGreenChannel,"OpenGL normal is linear Unity NormalMap with no extra green inversion or height conversion");
            Check(roughImporter.textureType==TextureImporterType.Default&&!roughImporter.sRGBTexture,"roughness is a linear scalar texture, not sRGB or a normal map");
            var normal=Resources.Load<Texture2D>("Vegetation/LeafNormal");var rough=Resources.Load<Texture2D>("Vegetation/LeafRoughness");Check(normal&&rough&&normal.width==1024&&rough.width==1024,"detail texture Resources resolve with full 1K grids");
            Texture2D rawNormal=null,rawRough=null,rawAlpha=null;
            try
            {
                rawNormal=new Texture2D(2,2,TextureFormat.RGBA32,false,true);rawRough=new Texture2D(2,2,TextureFormat.RGBA32,false,true);rawAlpha=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                Check(rawNormal.LoadImage(File.ReadAllBytes(Source+"/LeafNormal.png"))&&rawRough.LoadImage(File.ReadAllBytes(Source+"/LeafRoughness.png"))&&rawAlpha.LoadImage(File.ReadAllBytes(Source+"/LeafAtlas.png")),"Unity decodes actual prepared detail PNG data");
                var n=rawNormal.GetPixels32();var r=rawRough.GetPixels32();var alpha=rawAlpha.GetPixels32();var packed=normal.GetPixels();var actualRough=rough.GetPixels();int sample=-1;float score=0;bool backgrounds=true;int backgroundCount=0;
                for(int i=0;i<n.Length;i++)
                {
                    if(alpha[i].a<128){backgrounds&=n[i].r==128&&n[i].g==128&&n[i].b==255&&r[i].r==255&&r[i].g==255&&r[i].b==255;backgroundCount++;}
                    else if(alpha[i].a>240){float x=n[i].r/255f*2-1,y=n[i].g/255f*2-1,s=Mathf.Abs(x)+Mathf.Abs(y);if(x*x+y*y<.85f&&s>score){score=s;sample=i;}}
                }
                Check(backgrounds&&backgroundCount>100000,"actual normal background is flat128/128/255 and roughness background is one");
                Check(sample>=0&&score>.15f,"actual scanned leaf carries directional tangent normal detail");
                // Unity's RG-or-AG unpack convention supports both BC5 and DXT5nm:
                // X=R*A, Y=G. Check platform-imported packing, not only asset names.
                float expectedX=n[sample].r/255f*2-1,expectedY=n[sample].g/255f*2-1;
                float actualX=packed[sample].r*packed[sample].a*2-1,actualY=packed[sample].g*2-1;
                Check(Mathf.Abs(actualX-expectedX)<.15f&&Mathf.Abs(actualY-expectedY)<.15f,"actual platform normal packing decodes to source X/Y without handedness flip");
                float roughValue=r[sample].r/255f;Check(Mathf.Abs(actualRough[sample].r-roughValue)<.035f&&roughValue>.4f&&roughValue<.7f,"platform roughness sample retains source scalar without smoothness inversion/gamma packing");
                Debug.Log("BROADLEAF DETAIL PACKING normalFormat="+normal.format+" normalXY="+actualX+","+actualY+" sourceXY="+expectedX+","+expectedY+" rough="+actualRough[sample].r+" sourceRough="+roughValue);
            }
            finally
            {
                if(rawNormal)UnityEngine.Object.DestroyImmediate(rawNormal);if(rawRough)UnityEngine.Object.DestroyImmediate(rawRough);if(rawAlpha)UnityEngine.Object.DestroyImmediate(rawAlpha);
                normalImporter.isReadable=false;normalImporter.SaveAndReimport();roughImporter.isReadable=false;roughImporter.SaveAndReimport();
            }
            Check(!normalImporter.isReadable&&!roughImporter.isReadable,"runtime detail maps discard CPU copies after verification");
        }
        public static void RunLeafDetailMaps()
        {
            try
            {
                checks=0;var meta=JObject.Parse(File.ReadAllText("Data/Vegetation/broadleaf-assets.json"));var retained=new Dictionary<string,string>();
                foreach(var mesh in (JArray)meta["meshes"])
                {
                    string name=Path.GetFileNameWithoutExtension((string)mesh["file"]);retained[Destination+"/"+name+".asset"]=Hash(Destination+"/"+name+".asset");retained[Source+"/"+(string)mesh["file"]]=Hash(Source+"/"+(string)mesh["file"]);
                }
                foreach(string name in new[]{"LeafAtlas","BarkAtlas"}){retained[Destination+"/"+name+".png"]=Hash(Destination+"/"+name+".png");retained[Source+"/"+name+".png"]=Hash(Source+"/"+name+".png");}
                DetailTexture("LeafNormal",true);DetailTexture("LeafRoughness",false);VerifyDetailTextures(meta);AssetDatabase.SaveAssets();
                Check(retained.All(p=>Hash(p.Key)==p.Value),"all18 compiled/imported meshes, pivots and original colour/bark atlases retain exact bytes");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/broadleaf-leaf-detail.txt","PASS "+checks+" actual Unity source/hash/normal-packing/roughness/linear/import/resource/readback/preservation checks\nNative PBR appearance/performance remains a separate check.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            try
            {
                checks=0;var meta=JObject.Parse(File.ReadAllText("Data/Vegetation/broadleaf-assets.json"));Check((string)meta["format"]=="EarthUnscriptedBroadleafAssets2","original branching mesh format is recognised");
                Directory.CreateDirectory(Destination);int count=0;
                foreach(var item in (JArray)meta["meshes"])
                {
                    string file=(string)item["file"];if(file!=Path.GetFileName(file)||!file.EndsWith(".eumesh",StringComparison.Ordinal))throw new InvalidDataException("Invalid compiled mesh path");
                    string path=Source+"/"+file;Check(Hash(path)==(string)item["sha256"],"source hash "+file);
                    var mesh=Read(path);Check(mesh.vertexCount==(int)item["vertices"]&&mesh.triangles.Length/3==(int)item["triangles"],"geometry count "+file);
                    var pivotValues=new List<Vector3>();mesh.GetUVs(1,pivotValues);Check(pivotValues.Count==mesh.vertexCount,"card pivot channel exists "+file);
                    if((string)item["subset"]=="leaves")
                    {
                        int card=(int)item["lod"]==0?5:4;bool matching=pivotValues.Count%card==0;
                        for(int at=0;at<pivotValues.Count&&matching;at+=card)for(int k=1;k<card;k++)if(Vector3.Distance(pivotValues[at],pivotValues[at+k])>.000001f){matching=false;break;}
                        Check(matching,"all vertices share each leaf card pivot "+file);
                    }
                    else Check(pivotValues.All(p=>p==Vector3.zero),"bark pivot channel is zero "+file);
                    string output=Destination+"/"+Path.GetFileNameWithoutExtension(file)+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(output);
                    if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);}else AssetDatabase.CreateAsset(mesh,output);count++;
                }
                Check(count==18,"three variants, three LODs and two material subsets imported");
                foreach(var item in (JArray)meta["textures"])Check(Hash(Source+"/"+(string)item["file"])==(string)item["sha256"],"texture source hash "+(string)item["file"]);
                Texture("LeafAtlas",true);Texture("BarkAtlas",false);DetailTexture("LeafNormal",true);DetailTexture("LeafRoughness",false);VerifyDetailTextures(meta);AssetDatabase.SaveAssets();AssetDatabase.Refresh();
                for(int variant=0;variant<3;variant++)for(int lod=0;lod<3;lod++)
                {
                    int triangles=0;foreach(string subset in new[]{"bark","leaves"})
                    {
                        var mesh=Resources.Load<Mesh>("Vegetation/Broadleaf-"+variant+"-"+lod+"-"+subset);Check(mesh&&mesh.vertexCount>0,"resource resolves "+variant+"/"+lod+"/"+subset);triangles+=mesh.triangles.Length/3;
                    }
                    if(lod>0){int previous=new[]{"bark","leaves"}.Sum(subset=>Resources.Load<Mesh>("Vegetation/Broadleaf-"+variant+"-"+(lod-1)+"-"+subset).triangles.Length/3);Check(triangles<previous,"LOD triangle budget decreases "+variant+"/"+lod);}
                }
                for(int variant=0;variant<3;variant++)for(int lod=0;lod<3;lod++)
                {
                    var mesh=Resources.Load<Mesh>("Vegetation/Broadleaf-"+variant+"-"+lod+"-leaves");var pivotValues=new List<Vector3>();mesh.GetUVs(1,pivotValues);var points=mesh.vertices;var centre=pivotValues[0];float expected=(points[1]-points[0]).magnitude/(points[3]-points[0]).magnitude;
                    foreach(var aspect in new[]{new Vector2(2,10),new Vector2(5,4)})
                    {
                        var changed=points.Take(4).Select(p=>{p.y=centre.y+(p.y-centre.y)*aspect.x/aspect.y;return Vector3.Scale(p,new Vector3(aspect.x,aspect.y,aspect.x));}).ToArray();
                        float ratio=(changed[1]-changed[0]).magnitude/(changed[3]-changed[0]).magnitude;Check(Mathf.Abs(expected-ratio)<.00001f,"leaf photograph aspect survives h/r "+variant+"/"+lod+"/"+aspect);
                    }
                }
                var leaf=(TextureImporter)AssetImporter.GetAtPath(Destination+"/LeafAtlas.png");Check(leaf.DoesSourceTextureHaveAlpha()&&leaf.alphaIsTransparency&&leaf.mipMapsPreserveCoverage,"leaf alpha and mip coverage preserved for alpha-clipped cards");
                Check(Resources.Load<Texture2D>("Vegetation/LeafAtlas")&&Resources.Load<Texture2D>("Vegetation/BarkAtlas"),"shared foliage and bark textures resolve through Resources");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/broadleaf-assets.txt","PASS "+checks+" asset/hash/bounds/normal/channel/LOD/resource/alpha checks\nNo runtime placement or visual realism claim; native inspection and performance remain required.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
