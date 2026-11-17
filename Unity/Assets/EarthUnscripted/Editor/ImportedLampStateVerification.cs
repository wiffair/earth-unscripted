using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class ImportedLampStateVerification
    {
        public static void Batch(){try{Run();UnityEditor.EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);UnityEditor.EditorApplication.Exit(1);}}
        public static void Run()
        {
            int checks=0;void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;}
            var root=new GameObject("Actual imported lamp state fixture");var replacement=new GameObject("Newly built imported lamp fixture");
            ImportedCarAppearance car=null,next=null;var shaderFixtures=new List<Material>();
            try
            {
                Check(ImportedCarAppearance.TryBuild(root.transform,Color.blue,0,0,0,out car),"Actual included imported coupe appearance must build");
                const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
                List<Material> Lamps(ImportedCarAppearance value,string name)=>(List<Material>)typeof(ImportedCarAppearance).GetField(name,hidden).GetValue(value);
                var rear=Lamps(car,"rearLamps");var front=Lamps(car,"frontLamps");Check(rear.Count>0&&front.Count>0,"Real imported front/rear lamp materials exist");
                string Emission(Material material)=>material.HasProperty("_EmissiveColor")?"_EmissiveColor":"_EmissionColor";
                void Expect(List<Material> materials,Color expected,string label){foreach(var material in materials){var actual=material.GetColor(Emission(material));Check(Mathf.Abs(actual.r-expected.r)<.001f&&Mathf.Abs(actual.g-expected.g)<.001f&&Mathf.Abs(actual.b-expected.b)<.001f,label);}}
                Expect(rear,Color.black,"First daylight/released state is actually applied");Expect(front,Color.black,"First daylight front lamps are actually off");
                car.Lights(true,false);Expect(rear,Color.red*.8f,"Night tail illumination transition");Expect(front,new Color(.8f,.9f,1)*2,"Night front illumination transition");
                // A sentinel demonstrates no material write on a repeated state,
                // independent of private cache fields or an implementation count.
                rear[0].SetColor(Emission(rear[0]),Color.magenta);car.Lights(true,false);Expect(new List<Material>{rear[0]},Color.magenta,"Unchanged state performs no repeated material write");
                car.Lights(true,true);Expect(rear,Color.red*4,"Brake activation writes every rear lamp");
                car.Lights(false,true);Expect(rear,Color.red*4,"Braking remains illuminated after daylight transition");Expect(front,Color.black,"Daylight transition turns front emission off");
                car.Lights(false,false);Expect(rear,Color.black,"Brake release restores unlit daytime rear lamps");
                Check(ImportedCarAppearance.TryBuild(replacement.transform,Color.red,0,0,0,out next),"Replacement appearance builds independently");
                Expect(Lamps(next,"rearLamps"),Color.black,"New appearance applies its first false/false state despite previous car cache");Expect(Lamps(next,"frontLamps"),Color.black,"New appearance first front state is applied");
                foreach(var shader in new[]{Shader.Find("Universal Render Pipeline/Lit"),Resources.Load<Shader>("Graphics/HDSharedLit")})
                {
                    Check(shader,"Both supported lamp shader families are available");var material=new Material(shader);shaderFixtures.Add(material);
                    var setter=typeof(ImportedCarAppearance).GetMethod("SetLampEmission",BindingFlags.NonPublic|BindingFlags.Static);
                    setter.Invoke(null,new object[]{material,Color.red*4});Expect(new List<Material>{material},Color.red*4,"Shader consumes real brake emission property");
                    if(!material.HasProperty("_EmissiveColor"))Check(material.IsKeywordEnabled("_EMISSION"),"URP initial emission variant is enabled");
                    setter.Invoke(null,new object[]{material,Color.black});Expect(new List<Material>{material},Color.black,"Both renderer families release brake emission");
                }
            }
            finally{foreach(var material in shaderFixtures)UnityEngine.Object.DestroyImmediate(material);car?.ReleaseMaterials();next?.ReleaseMaterials();Geometry.ReleaseMeshes(root);Geometry.ReleaseMeshes(replacement);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(replacement);}
            Debug.Log("IMPORTED LAMP STATE PASS "+checks);
        }
    }
}
