using System;using System.Globalization;using System.IO;using System.Linq;using System.Threading.Tasks;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEditor;
namespace RoadRush.Editor
{
    // Can run against current geometry12 source: does not reference geometry13 DTOs.
    public static class WorldFieldJsonVerification
    {
        [Serializable] sealed class Profile {public double[] distances;}
        [Serializable] sealed class Arm {public double sourceDistance;}
        [Serializable] sealed class Anchor {public double parentSourceDistance,connectorSourceDistance;}
        [Serializable] sealed class Span {public double fromSourceDistance,toSourceDistance;}
        [Serializable] sealed class Fixture
        {
            public double[] values;public float[] floats;public Profile profile;public Arm arm;public Anchor anchor;public Span[] spans;public Vector3 point;public Vector2 direction;
            public string nullableString,emptyString="";public int[] nullableArray,emptyArray=Array.Empty<int>();
            [NonSerialized] public object runtimeOwner=new object();[SerializeField] int privateLeak=197;
            public double ThrowingGetter=>throw new InvalidOperationException("A getter was serialized");
        }
        public static void Run()
        {
            int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("FIELD JSON FAIL "+why);checks++;Debug.Log("FIELD JSON PASS "+why);}
            string career=Path.Combine(Application.persistentDataPath,"road-rush-save.json");string Hash()=>File.Exists(career)?Core.DataPackageRules.HashFile(career):"absent";string before=Hash();var oldCulture=CultureInfo.CurrentCulture;var outputs=new JArray();
            try
            {
                var values=new[]{0.0,1.2345678901234567,Math.Sqrt(2),10000.123456789123,BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1000)+1),.00012345678901234567,300000.1234567891,BitConverter.Int64BitsToDouble(long.MinValue)};
                float minusZero=BitConverter.ToSingle(BitConverter.GetBytes(int.MinValue),0);var floats=new[]{0f,.15f,.05f,.01f,12345.678f,float.Epsilon,minusZero};
                var original=new Fixture{values=values,floats=floats,profile=new Profile{distances=(double[])values.Clone()},arm=new Arm{sourceDistance=values[4]},anchor=new Anchor{parentSourceDistance=values[1],connectorSourceDistance=values[2]},spans=new[]{new Span{fromSourceDistance=values[4],toSourceDistance=values[5]}},point=new Vector3(1.2345678f,minusZero,12345.678f),direction=new Vector2(.15f,.05f)};
                bool Same(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
                bool FloatSame(float a,float b)=>BitConverter.ToInt32(BitConverter.GetBytes(a),0)==BitConverter.ToInt32(BitConverter.GetBytes(b),0);
                string compact=WorldFieldJson.Write(original);
                foreach(bool pretty in new[]{false,true})
                {
                    string json=WorldFieldJson.Write(original,pretty);var copy=WorldFieldJson.Read<Fixture>(json);var root=JObject.Parse(json);
                    Directory.CreateDirectory("Builds/Verification/WorldFieldJson");var detail=new JArray();for(int i=0;i<values.Length;i++)detail.Add(new JObject{{"index",i},{"originalR",values[i].ToString("R",CultureInfo.InvariantCulture)},{"copyR",copy.values[i].ToString("R",CultureInfo.InvariantCulture)},{"originalBits",BitConverter.DoubleToInt64Bits(values[i]).ToString("X16")},{"copyBits",BitConverter.DoubleToInt64Bits(copy.values[i]).ToString("X16")}});File.WriteAllText("Builds/Verification/WorldFieldJson/precision-diagnostic.json",new JObject{{"json",json},{"values",detail}}.ToString());
                    Check(copy.values.Length==values.Length&&values.Zip(copy.values,Same).All(x=>x),"actual Newtonsoft double bits incl nextafter1000 and signedzero pretty="+pretty);
                    Check(copy.profile.distances.Length==values.Length&&values.Zip(copy.profile.distances,Same).All(x=>x),"nested source station arrays preserve exact bits pretty="+pretty);
                    Check(Same(copy.arm.sourceDistance,original.arm.sourceDistance)&&Same(copy.anchor.parentSourceDistance,original.anchor.parentSourceDistance)&&Same(copy.anchor.connectorSourceDistance,original.anchor.connectorSourceDistance)&&Same(copy.spans[0].fromSourceDistance,original.spans[0].fromSourceDistance)&&Same(copy.spans[0].toSourceDistance,original.spans[0].toSourceDistance),"arm anchor span scalar bits preserve exactness pretty="+pretty);
                    Check(copy.floats.Length==floats.Length&&floats.Zip(copy.floats,FloatSame).All(x=>x)&&FloatSame(copy.point.y,original.point.y)&&FloatSame(copy.point.z,original.point.z),"float/vector fields preserve bits incl subnormal and signedzero pretty="+pretty);
                    Check(!root.ContainsKey("ThrowingGetter")&&!root.ContainsKey("runtimeOwner")&&!root.ContainsKey("privateLeak")&&((JObject)root["point"]).Properties().Select(p=>p.Name).SequenceEqual(new[]{"x","y","z"}),"only public instance nonruntime fields; no vector getter graph pretty="+pretty);
                    Check(copy.nullableString==null&&copy.nullableArray==null&&copy.emptyString==""&&copy.emptyArray.Length==0,"semantic null and empty values remain distinct pretty="+pretty);
                    Check(WorldFieldJson.Write(copy)==compact,"canonical compact text stable after writer reader pretty="+pretty);outputs.Add(new JObject{{"pretty",pretty},{"json",json}});
                }
                CultureInfo.CurrentCulture=new CultureInfo("fr-FR");Check(WorldFieldJson.Write(original)==compact&&Same(WorldFieldJson.Read<Fixture>(compact).arm.sourceDistance,values[4]),"culture cannot change number tokens or reader bits");CultureInfo.CurrentCulture=oldCulture;
                var parallel=new string[16];Parallel.For(0,parallel.Length,i=>parallel[i]=WorldFieldJson.Write(WorldFieldJson.Read<Fixture>(compact)));Check(parallel.All(x=>x==compact),"parallel field-contract cache and per-call serializers are deterministic");
                var clone=WorldFieldJson.Clone(original);clone.profile.distances[1]+=1;clone.spans[0].fromSourceDistance+=1;Check(Same(original.profile.distances[1],values[1])&&Same(original.spans[0].fromSourceDistance,values[4]),"source clone arrays and nested spans are exclusive");
                void Refuse(Action action,string why){bool refused=false;try{action();}catch(JsonException){refused=true;}catch(InvalidDataException){refused=true;}Check(refused,why);}
                Refuse(()=>WorldFieldJson.Read<Fixture>(compact.Substring(0,compact.Length-1)+",\"unexpected\":1}"),"unknown fields refused");Refuse(()=>WorldFieldJson.Read<Fixture>("{\"$type\":\"System.Version,mscorlib\"}"),"metadata cannot select a runtime type");Refuse(()=>WorldFieldJson.Read<Fixture>(compact+"{}"),"trailing JSON refused");Refuse(()=>WorldFieldJson.Read<Fixture>("{\"arm\":{\"sourceDistance\":\"1000\"}}"),"numeric strings cannot coerce station fields");Refuse(()=>WorldFieldJson.Write(new Arm{sourceDistance=double.NaN}),"nonfinite write refused");Refuse(()=>WorldFieldJson.Read<Arm>("{\"sourceDistance\":NaN}"),"nonfinite read refused");
                var region=new GeoRegion{id="download-local-alias",networkMapId="source-map",latitude=values[1],longitude=values[2]};string saved=WorldFieldJson.Write(region);var restored=WorldFieldJson.Read<GeoRegion>(saved);Check(restored.id==region.id&&restored.networkMapId==region.networkMapId&&Same(restored.latitude,region.latitude)&&Same(restored.longitude,region.longitude),"local source cache alias and double origin remain exact");Check(!JObject.Parse(saved).ContainsKey("PreparedRoadWidths")&&!JObject.Parse(saved).ContainsKey("HasServicePad"),"GeoRegion runtime owners and NonSerialized fields excluded");
                var unityControl=JsonUtility.FromJson<Fixture>(compact);bool unityReaderExact=values.Zip(unityControl.values,Same).All(x=>x);Check(Hash()==before,"protected real career unchanged");Directory.CreateDirectory("Builds/Verification/WorldFieldJson");File.WriteAllText("Builds/Verification/WorldFieldJson/receipt.json",new JObject{{"status","PASS"},{"checks",checks},{"unity",Application.unityVersion},{"actualManagedHelperInUnity",true},{"unityReaderExactDiagnostic",unityReaderExact},{"sourceStationMatrix","Builds/Verification/SourceStationJson/matrix.json"},{"payloads",outputs},{"scope","Helper capability only. Full13 typed preallocation/codec/cache/proof caller integration and native/physical admission remain pending."}}.ToString());EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}finally{CultureInfo.CurrentCulture=oldCulture;}
        }
    }
}
