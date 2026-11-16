using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    public static class AutomaticSceneryImport
    {
        const string Output="Assets/RoadRush/Resources/AutomaticWorld";
        public static void Import()
        {
            int exit=1;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-automaticSceneryPlan");
                if(at<0 || at+1>=args.Length)throw new ArgumentException("Pinned scenery import plan required");
                int reportAt=Array.IndexOf(args,"-automaticSceneryReport");
                if(reportAt<0 || reportAt+1>=args.Length || File.Exists(args[reportAt+1]))throw new IOException("Fresh scenery import report required");
                var plan=JObject.Parse(File.ReadAllText(args[at+1]));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string input="Assets/RoadRush/AutomaticEnvironmentSource";
                foreach(string path in Directory.GetFiles(input,"*",SearchOption.AllDirectories).Where(path=>path.EndsWith(".png")||path.EndsWith(".jpg")||path.EndsWith(".hdr")))
                {
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                    bool normal=path.Contains("nor_gl"),alpha=path.Contains("_alpha_"),view=path.Contains("TreeViews");
                    importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                    importer.sRGBTexture=!normal&&!alpha&&!path.EndsWith(".hdr");
                    importer.isReadable=view;importer.mipmapEnabled=true;
                    importer.wrapMode=view?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
                    importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=view?2:8;
                    importer.alphaIsTransparency=view||path.Contains("_diff_");
                    importer.alphaSource=alpha?TextureImporterAlphaSource.FromGrayScale:TextureImporterAlphaSource.FromInput;
                    importer.mipMapsPreserveCoverage=view||alpha;importer.alphaTestReferenceValue=.3f;
                    importer.maxTextureSize=path.EndsWith(".hdr")?2048:1024;
                    if(view)importer.textureCompression=TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                string modelPath=(string)plan["treeModel"];
                var modelImporter=(ModelImporter)AssetImporter.GetAtPath(modelPath);
                modelImporter.globalScale=1;modelImporter.useFileScale=true;modelImporter.bakeAxisConversion=true;
                modelImporter.importTangents=ModelImporterTangents.CalculateMikk;modelImporter.SaveAndReimport();
                Directory.CreateDirectory(Output+"/Materials");AssetDatabase.Refresh();
                var resource=AssetDatabase.LoadAssetAtPath<AutomaticSceneryResources>(Output+"/Scenery.asset");
                if(!resource){resource=ScriptableObject.CreateInstance<AutomaticSceneryResources>();AssetDatabase.CreateAsset(resource,Output+"/Scenery.asset");}
                resource.surfaceShader=Shader.Find("RoadRush/Automatic Photographic Surface");
                resource.foliageShader=Shader.Find("RoadRush/Automatic Photographic Foliage");
                resource.billboardShader=Shader.Find("RoadRush/Automatic Tree Views");
                foreach(var shader in new[]{resource.surfaceShader,resource.foliageShader,resource.billboardShader})
                    if(!shader || ShaderUtil.ShaderHasError(shader))throw new InvalidDataException("Scenery shader failed: "+shader?.name);
                foreach(var item in (JArray)plan["surfaces"])
                {
                    var surface=new AutomaticSceneryResources.Surface{name=(string)item["name"],
                        albedo=AssetDatabase.LoadAssetAtPath<Texture2D>((string)item["albedo"]),
                        normal=AssetDatabase.LoadAssetAtPath<Texture2D>((string)item["normal"]),
                        metres=new Vector2((float)item["metres"][0],(float)item["metres"][1])};
                    if(!surface.albedo||!surface.normal)throw new InvalidDataException("Missing photographic surface "+surface.name);
                    typeof(AutomaticSceneryResources).GetField(surface.name).SetValue(resource,surface);
                }
                resource.sky=AssetDatabase.LoadAssetAtPath<Texture2D>((string)plan["sky"]);
                var skyShader=Shader.Find("Skybox/Panoramic");if(!skyShader)throw new InvalidDataException("Panoramic sky shader missing");
                var skyMaterial=new Material(skyShader){name="Photographic clouds"};skyMaterial.SetTexture("_MainTex",resource.sky);skyMaterial.SetFloat("_Exposure",1.05f);
                string skyPath=Output+"/Materials/PhotographicSky.mat";if(AssetDatabase.LoadMainAssetAtPath(skyPath))AssetDatabase.DeleteAsset(skyPath);AssetDatabase.CreateAsset(skyMaterial,skyPath);resource.skyMaterial=skyMaterial;
                var views=((JArray)plan["treeViews"]).Select(path=>AssetDatabase.LoadAssetAtPath<Texture2D>((string)path)).ToArray();
                if(views.Length!=24 || views.Any(image=>!image||image.width!=512||image.height!=1024))throw new InvalidDataException("Tree view image dimensions");
                var array=new Texture2DArray(512,1024,24,TextureFormat.RGBA32,true,false){name="Photographic fir views",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=2};
                for(int slice=0;slice<views.Length;slice++)for(int mip=0;mip<array.mipmapCount;mip++)array.SetPixels(views[slice].GetPixels(mip),slice,mip);
                array.Apply(false,false);
                string arrayPath=Output+"/FirViews.asset";if(AssetDatabase.LoadMainAssetAtPath(arrayPath))AssetDatabase.DeleteAsset(arrayPath);
                AssetDatabase.CreateAsset(array,arrayPath);resource.treeViews=array;
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);var materials=new Dictionary<string,Material>();
                var variants=new List<AutomaticSceneryResources.TreeModel>();
                foreach(char variant in new[]{'a','b','c'})
                {
                    var filter=prefab.GetComponentsInChildren<MeshFilter>(true).Single(item=>item.name=="Fir_"+variant+"_LOD1");
                    var renderer=filter.GetComponent<Renderer>();var mapped=new List<Material>();
                    foreach(var original in renderer.sharedMaterials)
                    {
                        string name=original.name;
                        if(!materials.TryGetValue(name,out var material))
                        {
                            string folder=(string)plan["treeTextures"];
                            var diffuse=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+name+"_diff_1k.png");
                            var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+name+"_nor_gl_1k.png");
                            var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+name+"_alpha_1k.png");
                            bool dead=name.Contains("dead_branches");
                            if(dead){diffuse=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/fir_tree_01_twig_diff_1k.png");mask=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/fir_tree_01_twig_alpha_1k.png");}
                            if(!diffuse)throw new InvalidDataException("Missing tree material photograph "+name);
                            material=new Material(resource.foliageShader){name=name,enableInstancing=true};
                            material.SetTexture("_BaseMap",diffuse);if(normal)material.SetTexture("_BumpMap",normal);if(mask)material.SetTexture("_AlphaMap",mask);
                            material.SetColor("_BaseColor",dead?new Color(.65f,.45f,.28f):Color.white);
                            string path=Output+"/Materials/"+name+".mat";if(AssetDatabase.LoadMainAssetAtPath(path))AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(material,path);materials.Add(name,material);
                        }
                        mapped.Add(material);
                    }
                    Matrix4x4 local=prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                    var bounds=filter.sharedMesh.bounds;Vector3 minimum=Vector3.one*float.MaxValue,maximum=Vector3.one*float.MinValue;
                    foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
                    {var point=local.MultiplyPoint3x4(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));minimum=Vector3.Min(minimum,point);maximum=Vector3.Max(maximum,point);}
                    float height=maximum.y-minimum.y;if(height<5||height>40)throw new InvalidDataException("Tree import axis/scale is invalid: "+height);
                    local=Matrix4x4.Translate(new Vector3(-(minimum.x+maximum.x)*.5f,-minimum.y,-(minimum.z+maximum.z)*.5f))*local;
                    variants.Add(new AutomaticSceneryResources.TreeModel{mesh=filter.sharedMesh,local=local,materials=mapped.ToArray(),height=height});
                }
                resource.treeModels=variants.ToArray();resource.credits=plan["credits"].ToString();EditorUtility.SetDirty(resource);AssetDatabase.SaveAssets();
                File.WriteAllText(args[reportAt+1],new JObject{["passed"]=true,["treeVariants"]=variants.Count,["treeViews"]=views.Length,["credits"]=plan["credits"]}.ToString());
                exit=0;
            }
            catch(Exception error){Debug.LogException(error);}
            finally{EditorApplication.Exit(exit);}
        }
    }
}
