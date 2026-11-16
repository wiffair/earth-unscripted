using System;using System.IO;using System.Linq;using System.Security.Cryptography;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEditor;
namespace RoadRush.Editor
{
    // Engine serializer capability proof, independent of unimported geometry13 DTOs.
    public static class SourceStationJsonVerification
    {
        [Serializable] sealed class Profile{public double[] distances;}
        [Serializable] sealed class Arm{public double sourceDistance;}
        [Serializable] sealed class Anchor{public double parentSourceDistance,connectorSourceDistance;}
        [Serializable] sealed class Span{public double fromSourceDistance,toSourceDistance;}
        [Serializable] sealed class Fixture{public double[] values;public Profile profile;public Arm arm;public Anchor anchor;public Span[] spans;}
        public static void Diagnose()
        {
            try
            {
                double[] values={0,1.2345678901234567,Math.Sqrt(2),10000.123456789123,BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1000)+1),.00012345678901234567,300000.1234567891};
                var fixture=new Fixture{values=values,profile=new Profile{distances=values},arm=new Arm{sourceDistance=values[1]},anchor=new Anchor{parentSourceDistance=values[2],connectorSourceDistance=values[3]},spans=new[]{new Span{fromSourceDistance=values[4],toSourceDistance=values[5]}}};
                var cases=new JArray();bool preciseUnityReader=false,preciseNewtonsoft=false;
                foreach(string writer in new[]{"Unity","Newtonsoft"})
                {
                    string json=writer=="Unity"?JsonUtility.ToJson(fixture):Newtonsoft.Json.JsonConvert.SerializeObject(fixture);
                    foreach(string reader in new[]{"Unity","Newtonsoft"})
                    {
                        var copy=reader=="Unity"?JsonUtility.FromJson<Fixture>(json):Newtonsoft.Json.JsonConvert.DeserializeObject<Fixture>(json);var differences=new JArray();
                        void Compare(string field,double a,double b){if(BitConverter.DoubleToInt64Bits(a)!=BitConverter.DoubleToInt64Bits(b))differences.Add(new JObject{{"field",field},{"originalR",a.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},{"roundtripR",b.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},{"absoluteError",Math.Abs(a-b)}});}
                        for(int i=0;i<values.Length;i++){Compare("values["+i+"]",values[i],copy.values[i]);Compare("profile.distances["+i+"]",values[i],copy.profile.distances[i]);}
                        Compare("arm.sourceDistance",fixture.arm.sourceDistance,copy.arm.sourceDistance);Compare("anchor.parentSourceDistance",fixture.anchor.parentSourceDistance,copy.anchor.parentSourceDistance);Compare("anchor.connectorSourceDistance",fixture.anchor.connectorSourceDistance,copy.anchor.connectorSourceDistance);Compare("span.fromSourceDistance",fixture.spans[0].fromSourceDistance,copy.spans[0].fromSourceDistance);Compare("span.toSourceDistance",fixture.spans[0].toSourceDistance,copy.spans[0].toSourceDistance);
                        cases.Add(new JObject{{"writer",writer},{"reader",reader},{"exactBits",differences.Count==0},{"json",json},{"differences",differences}});if(writer=="Newtonsoft"&&reader=="Unity")preciseUnityReader=differences.Count==0;if(writer=="Newtonsoft"&&reader=="Newtonsoft")preciseNewtonsoft=differences.Count==0;
                    }
                }
                Directory.CreateDirectory("Builds/Verification/SourceStationJson");File.WriteAllText("Builds/Verification/SourceStationJson/matrix.json",new JObject{{"unity",Application.unityVersion},{"preciseNewtonsoftWriterUnityReader",preciseUnityReader},{"preciseNewtonsoftWriterNewtonsoftReader",preciseNewtonsoft},{"cases",cases},{"scope","Serializer capability matrix only; full geometry13 DTO/codec/cache integration remains unaccepted."}}.ToString());Debug.Log("SOURCE JSON MATRIX preciseRWriterUnityReader="+preciseUnityReader+" preciseRWriterRReader="+preciseNewtonsoft);EditorApplication.Exit(preciseNewtonsoft?0:1);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Run()=>WorldFieldJsonVerification.Run();
        // Historical strict control: this intentionally exposes the current
        // Unity reader's precision loss. The supported path is tested by Run.
        public static void DemonstrateUnityReaderLoss()
        {
            int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Debug.Log("SOURCE JSON PASS "+why);}
            string save=Path.Combine(Application.persistentDataPath,"road-rush-save.json");string Hash()=>File.Exists(save)?RoadRush.Core.DataPackageRules.HashFile(save):null;string before=Hash();
            try
            {
                double[] digits={0,1.2345678901234567,Math.Sqrt(2),10000.123456789123,BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1000)+1),.00012345678901234567,300000.1234567891};
                var fixture=new Fixture{values=digits,profile=new Profile{distances=(double[])digits.Clone()},arm=new Arm{sourceDistance=digits[1]},anchor=new Anchor{parentSourceDistance=digits[2],connectorSourceDistance=digits[3]},spans=new[]{new Span{fromSourceDistance=digits[4],toSourceDistance=digits[5]}}};
                bool Same(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
                var output=new JArray();
                foreach(bool pretty in new[]{false,true})
                {
                    string json=JsonUtility.ToJson(fixture,pretty);var copy=JsonUtility.FromJson<Fixture>(json);
                    Check(copy.values.Length==digits.Length&&digits.Zip(copy.values,Same).All(x=>x),"double values preserve exact bits pretty="+pretty);
                    Check(copy.profile.distances.Length==digits.Length&&digits.Zip(copy.profile.distances,Same).All(x=>x),"profile source distance arrays preserve exact bits pretty="+pretty);
                    Check(Same(copy.arm.sourceDistance,fixture.arm.sourceDistance)&&Same(copy.anchor.parentSourceDistance,fixture.anchor.parentSourceDistance)&&Same(copy.anchor.connectorSourceDistance,fixture.anchor.connectorSourceDistance),"arm and synthetic anchor scalar stations preserve exact bits pretty="+pretty);
                    Check(copy.spans.Length==1&&Same(copy.spans[0].fromSourceDistance,fixture.spans[0].fromSourceDistance)&&Same(copy.spans[0].toSourceDistance,fixture.spans[0].toSourceDistance),"nested span stations preserve exact bits pretty="+pretty);output.Add(json);
                }
                Check(Hash()==before,"real career unchanged");Directory.CreateDirectory("Builds/Verification/SourceStationJson");
                File.WriteAllText("Builds/Verification/SourceStationJson/receipt.json",new JObject{{"status","PASS"},{"checks",checks},{"unity",Application.unityVersion},{"actualEngineJsonUtility",true},{"payloads",output},{"scope","Actual Unity serializer capability for matching double array/scalar/nested span field shapes. Geometry13 actual DTO/codec/preallocation/source-phase and native world acceptance remain pending."}}.ToString());EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
