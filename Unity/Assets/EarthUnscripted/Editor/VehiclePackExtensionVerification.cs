using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    /// <summary>Pure admission tests against the actual game parser. No library activation or extraction.</summary>
    public static class VehiclePackExtensionVerification
    {
        static int checks, serial;
        sealed class FieldsOnly:DefaultContractResolver
        {protected override List<MemberInfo> GetSerializableMembers(Type type)=>type.GetFields(BindingFlags.Instance|BindingFlags.Public).Cast<MemberInfo>().ToList();}
        static string Fingerprint(VehiclePack pack)=>JsonConvert.SerializeObject(pack,new JsonSerializerSettings{ContractResolver=new FieldsOnly()});
        static void Check(bool valid,string name)
        {if(!valid)throw new Exception("VEHICLE PACK EXTENSION FAIL: "+name);checks++;}
        static void Reject(Action parse,string name,string reason=null)
        {
            try{parse();}catch(Exception error)when(error is InvalidDataException||error is JsonException)
            {Check(reason==null||error.Message.IndexOf(reason,StringComparison.OrdinalIgnoreCase)>=0,name+" fails at its intended admission guard: "+error.Message);return;}
            throw new Exception("VEHICLE PACK EXTENSION FAIL: accepted "+name);
        }
        static byte[] Bytes(JObject json)=>Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        static byte[] Alter(byte[] source,int offset,params byte[] values)
        {var copy=(byte[])source.Clone();Array.Copy(values,0,copy,offset,values.Length);return copy;}
        static byte[] Wav(int rate,int samples)
        {
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);
                for(int i=0;i<samples;i++)writer.Write(i==0?short.MinValue:i==1?short.MaxValue:(short)0);
                return memory.ToArray();
            }
        }
        static byte[] AppendChunk(byte[] source,string tag,byte[] payload,bool padding=true)
        {
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory))
            {writer.Write(source);writer.Write(Encoding.ASCII.GetBytes(tag));writer.Write(payload.Length);writer.Write(payload);if(padding&&(payload.Length&1)!=0)writer.Write((byte)0);writer.Flush();var result=memory.ToArray();BitConverter.GetBytes(result.Length-8).CopyTo(result,4);return result;}
        }
        static void Pcm()
        {
            foreach(int rate in new[]{22050,44100,48000})
            {
                int minimum=rate/4;var decoded=VehiclePackPcm.Decode(Wav(rate,minimum));
                Check(decoded.SampleRate==rate&&decoded.Samples.Length==minimum,"PCM minimum duration at "+rate);
                Check(decoded.Samples[0]==-1&&decoded.Samples[1]==32767f/32768f,"PCM signed normalization at "+rate);
                Reject(()=>VehiclePackPcm.Decode(Wav(rate,minimum-1)),"PCM shorter than minimum","duration");
            }
            Check(VehiclePackPcm.Decode(Wav(48000,48000*30)).Samples.Length==48000*30,"PCM maximum duration inclusive");
            Reject(()=>VehiclePackPcm.Decode(Wav(48000,48000*30+1)),"PCM duration above maximum","duration");
            var wav=Wav(44100,11025);
            Reject(()=>VehiclePackPcm.Decode(null),"null WAV");Reject(()=>VehiclePackPcm.Decode(new byte[43]),"short WAV");
            Reject(()=>VehiclePackPcm.Decode(new byte[VehiclePackPcm.MaximumBytes+1]),"WAV byte budget");
            foreach(var mutation in new[]{Alter(wav,0,0),Alter(wav,4,0,0,0,0),Alter(wav,20,3,0),Alter(wav,22,2,0),Alter(wav,24,1,0,0,0),Alter(wav,28,1,0,0,0),Alter(wav,32,1,0),Alter(wav,34,24,0),Alter(wav,40,255,255,255,255),Alter(wav,40,1,0,0,0)})
                Reject(()=>VehiclePackPcm.Decode(mutation),"malformed WAV header/codec/chunk");
            Reject(()=>VehiclePackPcm.Decode(wav.Take(wav.Length-1).ToArray()),"truncated WAV");
            Reject(()=>VehiclePackPcm.Decode(AppendChunk(wav,"fmt ",wav.Skip(20).Take(16).ToArray())),"duplicate PCM format","one 16-byte");
            Reject(()=>VehiclePackPcm.Decode(AppendChunk(wav,"data",new byte[]{0,0})),"duplicate PCM data","duplicate");
            Reject(()=>VehiclePackPcm.Decode(AppendChunk(wav,"exec",new byte[]{0,0})),"unknown PCM chunk","Unsupported");
            Reject(()=>VehiclePackPcm.Decode(AppendChunk(wav,"JUNK",new byte[]{0},false)),"missing odd chunk pad","padding");
            Check(VehiclePackPcm.Decode(AppendChunk(wav,"JUNK",new byte[]{0})).Samples.Length==11025,"well-bounded padded metadata accepted");
            var empty=Wav(44100,0);Reject(()=>VehiclePackPcm.Decode(empty),"empty WAV data","data");
        }
        static Dictionary<string,byte[]> ReadFiles(string path)
        {
            var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
            using(var zip=ZipFile.OpenRead(path))foreach(var entry in zip.Entries)
                using(var source=entry.Open())using(var output=new MemoryStream()){source.CopyTo(output);files.Add(entry.FullName,output.ToArray());}
            return files;
        }
        static string Write(string directory,JObject manifest,JObject geometry,byte[] wav,Action<Dictionary<string,byte[]>> edit=null)
        {
            byte[] mesh=Bytes(geometry);manifest=(JObject)manifest.DeepClone();manifest["meshSha256"]=VehiclePackFormat.Digest(mesh);
            var files=new Dictionary<string,byte[]>{{"manifest.json",Bytes(manifest)},{"mesh.json",mesh},{"engine-exhaust.wav",wav},{"engine-mechanical.wav",wav}};edit?.Invoke(files);
            string path=Path.Combine(directory,(serial++)+".eucar");using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(var item in files)
                using(var stream=zip.CreateEntry(item.Key).Open())stream.Write(item.Value,0,item.Value.Length);
            return path;
        }
        static JObject Geometry()
        {
            var parts=new JArray();foreach(string attachment in new[]{"body","wheel0","wheel1","wheel2","wheel3","wiper0","wiper1"})
                parts.Add(new JObject{{"name",attachment},{"attachment",attachment},{"material",0},{"vertices",new JArray(0,0,0,.1,0,0,0,.1,0)},{"triangles",new JArray(0,1,2)}});
            return new JObject{{"materials",new JArray(new JObject{{"colour",new JArray(.4,.1,.1)},{"metallic",.3},{"smoothness",.8},{"paint",false},{"finish",new JObject{{"kind","layered-candy-v1"},{"transmission",new JArray(.7,.2,.1)},{"opticalThickness",1},{"flakeStrength",.5},{"flakeScalePerMetre",500},{"clearcoat",1}}}})},{"parts",parts}};
        }
        static JObject Manifest(JObject legacy,byte[] wav)
        {
            var manifest=(JObject)legacy.DeepClone();manifest.Remove("textureSha256");manifest["id"]="earthunscripted.verification-extension";manifest["priceCredits"]=50000;
            manifest["requirements"]=new JObject{{"features",new JArray("engine-audio-pcm-v1","layered-candy-paint-v1","authored-wipers-v1")}};
            manifest["wipers"]=new JObject{{"formatVersion",1},{"pivots",new JArray(new JArray(-.4,1.2,1),new JArray(.4,1.2,1))},{"sweepDegrees",new JArray(70,-70)}};
            var loop=new JObject{{"file","engine-exhaust.wav"},{"sha256",VehiclePackFormat.Digest(wav)},{"referenceRpm",1800},{"referenceRpmEstimated",true},{"gain",.8}};
            var mechanical=(JObject)loop.DeepClone();mechanical["file"]="engine-mechanical.wav";mechanical["gain"]=.6;
            manifest["engineAudio"]=new JObject{{"formatVersion",1},{"exhaust",loop},{"mechanical",mechanical},{"minimumPitch",.5},{"maximumPitch",2.5},{"blendStartThrottle",.1},{"blendEndThrottle",.8}};
            return manifest;
        }
        static void Response(string directory,JObject legacy,JObject geometry,byte[] wav)
        {
            var old=VehiclePackFormat.Read(Write(directory,legacy,geometry,wav)).EngineAudio;
            bool Bits(float a,float b)=>BitConverter.GetBytes(a).SequenceEqual(BitConverter.GetBytes(b));
            for(float rpm=0;rpm<=7500;rpm+=17)Check(Bits(RecordedPackEngine.PitchTarget(rpm,old,old.Exhaust),Mathf.Clamp(rpm/old.Exhaust.ReferenceRpm,old.MinimumPitch,old.MaximumPitch)),"legacy exact pitch at "+rpm);
            foreach(float volume in new[]{0f,.2f,.5f,1f})for(int i=0;i<=100;i++)
            {float blend=i/100f;RecordedPackEngine.TargetGains(old,blend,volume,out float e,out float m);Check(Bits(e,Mathf.Clamp01(volume)*old.Exhaust.Gain*(1-.5f*blend))&&Bits(m,Mathf.Clamp01(volume)*old.Mechanical.Gain*(.1f+.9f*blend)),"legacy exact mix endpoints/interior");}
            var response=(JObject)legacy.DeepClone();((JArray)response["requirements"]["features"]).Add("engine-audio-response-v1");var a=(JObject)response["engineAudio"];a["pitchResponseExponent"]=.35;a["exhaustLoadRetention"]=1;a["minimumPitch"]=.85;a["maximumPitch"]=2.2;a["exhaust"]["referenceRpm"]=900;a["mechanical"]["referenceRpm"]=900;a["exhaust"]["gain"]=.85;a["mechanical"]["gain"]=.15;
            var parsed=VehiclePackFormat.Read(Write(directory,response,geometry,wav)).EngineAudio;
            float previous=0;for(int rpm=900;rpm<=6800;rpm+=5){float pitch=RecordedPackEngine.PitchTarget(rpm,parsed,parsed.Exhaust);Check(pitch>previous&&pitch<parsed.MaximumPitch,"compressed perceptual RPM rises without early saturation "+rpm);previous=pitch;}
            Check(RecordedPackEngine.PitchTarget(900,parsed,parsed.Exhaust)==1,"estimated reference RPM retained at unity pitch");
            foreach(float volume in new[]{0f,.2f,1f})for(int i=0;i<=1000;i++){RecordedPackEngine.TargetGains(parsed,i/1000f,volume,out float e,out float m);Check(e>=0&&m>=0&&e+m<=1.000001f,"response target peak budget at every blend");Check(e<=.850001f&&m<=.150001f,"candidate per-voice bounds cannot exceed unit combined gain even during smoothing");}
            foreach(string field in new[]{"pitchResponseExponent","exhaustLoadRetention"})foreach(double value in field=="pitchResponseExponent"?new[]{double.NaN,double.PositiveInfinity,-1,0,.249,1.001}:new[]{double.NaN,double.PositiveInfinity,-.001,1.001})
            {var bad=(JObject)response.DeepClone();bad["engineAudio"][field]=new JValue(value);Reject(()=>VehiclePackFormat.Read(Write(directory,bad,geometry,wav)),"bounded finite "+field+" "+value);}
            var missing=(JObject)response.DeepClone();((JArray)missing["requirements"]["features"]).Where(t=>(string)t=="engine-audio-response-v1").ToList().ForEach(t=>t.Remove());Reject(()=>VehiclePackFormat.Read(Write(directory,missing,geometry,wav)),"response controls require new capability","without declaring");
            var unsafeGain=(JObject)response.DeepClone();unsafeGain["engineAudio"]["mechanical"]["gain"]=.16;Reject(()=>VehiclePackFormat.Read(Write(directory,unsafeGain,geometry,wav)),"retained exhaust plus load cannot exceed master peak budget","target gain");
            Check(parsed.Exhaust.ReferenceRpmEstimated&&parsed.Mechanical.ReferenceRpmEstimated,"perceptual mapping does not invent tachometer provenance");
        }
        static void Extensions(string directory,JObject manifest,JObject geometry,byte[] wav)
        {
            VehiclePack Parse(JObject m,JObject g=null,Action<Dictionary<string,byte[]>> edit=null)=>VehiclePackFormat.Read(Write(directory,m,g??geometry,wav,edit));
            var good=Parse(manifest);Check(good.Settings.Price==50000,"extension price retained");
            Check(good.Wipers.Pivots.Length==2&&good.Wipers.SweepDegrees.SequenceEqual(new[]{70f,-70f}),"authored pivot/sweep metadata retained");
            Check(good.Parts.Count(part=>part.Attachment=="wiper0")==1&&good.Parts.Count(part=>part.Attachment=="wiper1")==1,"both authored mesh attachments admitted");
            Check(good.Materials[0].Finish.Clearcoat==1&&!good.Settings.SupportsPaint,"fixed layered paint preserved");
            Check(good.EngineAudio.Exhaust.SampleRate==44100&&good.EngineAudio.Mechanical.ReferenceRpmEstimated,"real PCM and declared RPM admitted");
            Check(good.EngineAudio.PitchResponseExponent==1&&good.EngineAudio.ExhaustLoadRetention==.5f,"legacy audio response defaults preserved");
            Response(directory,manifest,geometry,wav);
            var bank=(JObject)manifest.DeepClone();((JArray)bank["requirements"]["features"]).Add("engine-audio-rpm-bank-v1");
            bank["engineAudio"]["rpmProfile"]="road-v8";bank["engineAudio"]["rpmBlendStart"]=1400;bank["engineAudio"]["rpmBlendEnd"]=1900;
            var bankAudio=Parse(bank).EngineAudio;Check(bankAudio.RpmProfile=="road-v8"&&bankAudio.RpmBlendStart==1400,"opt-in RPM bank admitted");
            RecordedPackEngine.TargetGains(bankAudio,0,1,out float idleExhaust,out float idleLoad);Check(idleExhaust>0&&idleLoad==0,"high-RPM bank is silent at idle");
            RecordedPackEngine.TargetGains(bankAudio,1,1,out float highExhaust,out float highLoad);Check(highExhaust==0&&highLoad>0,"pitch-clamped idle bank is silent at high RPM");
            for(int i=0;i<=100;i++){RecordedPackEngine.TargetGains(bankAudio,i/100f,1,out float e,out float m);Check(e>=0&&m>=0&&e+m<=1.000001f,"RPM crossfade stays within gain budget");}
            var unsupported=(JObject)bank.DeepClone();unsupported["engineAudio"]["rpmProfile"]="unknown";Reject(()=>Parse(unsupported),"unknown RPM profile rejected");
            var reversed=(JObject)bank.DeepClone();reversed["engineAudio"]["rpmBlendEnd"]=1200;Reject(()=>Parse(reversed),"reversed RPM interval rejected");
            var undeclared=(JObject)bank.DeepClone();((JArray)undeclared["requirements"]["features"]).Where(t=>(string)t=="engine-audio-rpm-bank-v1").ToList().ForEach(t=>t.Remove());Reject(()=>Parse(undeclared),"RPM metadata needs explicit capability");
            Check(good.EngineAudio.RpmProfile=="vehicle"&&good.EngineAudio.RpmBlendEnd==0,"legacy packs do not acquire an RPM bank");
            var tyre=(JObject)manifest.DeepClone();((JArray)tyre["requirements"]["features"]).Add("tyre-surface-audio-v1");tyre["physics"]["tyreSoundProfile"]="offroad";
            Check(Parse(tyre).Settings.TyreSoundProfile=="offroad","explicit tyre acoustic profile admitted");
            var badTyre=(JObject)tyre.DeepClone();badTyre["physics"]["tyreSoundProfile"]="unknown";Reject(()=>Parse(badTyre),"unknown tyre acoustic profile rejected");
            var missingTyre=(JObject)tyre.DeepClone();((JArray)missingTyre["requirements"]["features"]).Where(t=>(string)t=="tyre-surface-audio-v1").ToList().ForEach(t=>t.Remove());Reject(()=>Parse(missingTyre),"tyre metadata requires explicit capability");
            Check(good.Settings.TyreSoundProfile=="auto","legacy acoustic profile remains automatic");
            var roadRpm=new EarthUnscripted.Core.RoadV8EngineRpm();
            for(float speed=0;speed<=60;speed+=.1f){float rpm=roadRpm.Target(speed,.5f,.375f);Check(!float.IsNaN(rpm)&&rpm>=750&&rpm<=6200,"road V8 bounded throughout acceleration");}
            for(float speed=60;speed>=0;speed-=.1f){float rpm=roadRpm.Target(speed,.1f,.375f);Check(!float.IsNaN(rpm)&&rpm>=750&&rpm<=6200,"road V8 bounded throughout deceleration");}
            roadRpm.Reset();roadRpm.Target(25/2.23694f,.5f,.375f);int gear=roadRpm.Gear;for(int i=0;i<40;i++)roadRpm.Target((24.9f+(i%2)*.2f)/2.23694f,.5f,.375f);Check(roadRpm.Gear==gear,"shift hysteresis avoids threshold chatter");
            roadRpm.Reset();roadRpm.Target(75/2.23694f,0,.375f);int cruiseGear=roadRpm.Gear;
            for(int i=0;i<120;i++)roadRpm.Target(75/2.23694f,i%2,.375f);
            Check(roadRpm.Gear==cruiseGear,"road V8 holds its cruising gear across throttle changes instead of hunting");
            Check(roadRpm.Target(float.NaN,float.PositiveInfinity,float.NaN)>=750,"nonfinite sound inputs are contained");

            void Bad(string name,Action<JObject> mutate,string reason=null)
            {var m=(JObject)manifest.DeepClone();mutate(m);Reject(()=>Parse(m),name,reason);}
            Bad("unknown required feature",m=>m["requirements"]["features"]=new JArray("future-feature"),"newer game");
            Bad("duplicate required feature",m=>m["requirements"]["features"]=new JArray("authored-wipers-v1","authored-wipers-v1"),"duplicate");
            Bad("empty requirements",m=>m["requirements"]["features"]=new JArray(),"requirements");
            Bad("non-text requirement",m=>m["requirements"]["features"]=new JArray(2),"text");
            foreach(string feature in new[]{"engine-audio-pcm-v1","layered-candy-paint-v1","authored-wipers-v1"})
                Bad("omitted "+feature,m=>((JArray)m["requirements"]["features"]).Where(t=>(string)t==feature).ToList().ForEach(t=>t.Remove()),"without declaring");
            Bad("omitted requirements",m=>m.Remove("requirements"),"without declaring");
            foreach(var gains in new[]{new[]{.4,1.0},new[]{1.0,.1}})
                Bad("unsafe target-gain endpoint "+gains[0]+"/"+gains[1],m=>{m["engineAudio"]["exhaust"]["gain"]=gains[0];m["engineAudio"]["mechanical"]["gain"]=gains[1];},"target gain");
            foreach(var gains in new[]{new[]{.92,.53},new[]{.8,.6},new[]{1.0,0.0},new[]{0.0,1.0}})
            {
                var m=(JObject)manifest.DeepClone();m["engineAudio"]["exhaust"]["gain"]=gains[0];m["engineAudio"]["mechanical"]["gain"]=gains[1];var audio=Parse(m).EngineAudio;
                Check(audio.Exhaust.Gain==(float)gains[0]&&audio.Mechanical.Gain==(float)gains[1],"safe actual target endpoints admitted "+gains[0]+"/"+gains[1]);
            }
            Bad("wipers missing but mesh present",m=>m.Remove("wipers"),"metadata");
            Bad("malformed wiper object",m=>m["wipers"]=2,"authored wipers");
            Bad("unsupported wiper version",m=>m["wipers"]["formatVersion"]=2);
            Bad("missing wiper pivot",m=>m["wipers"]["pivots"]=new JArray(new JArray(0,1,1)),"Two wiper");
            Bad("malformed pivot width",m=>m["wipers"]["pivots"][0]=new JArray(0,1),"pivot");
            foreach(var pivot in new[]{new JArray(2.01,1,1),new JArray(0,-.01,1),new JArray(0,3.01,1),new JArray(0,1,3.01)})
                Bad("out-of-bounds authored pivot",m=>m["wipers"]["pivots"][0]=pivot.DeepClone(),"range");
            Bad("wiper zero sweep",m=>m["wipers"]["sweepDegrees"]=new JArray(0,70),"10 degrees");
            Bad("wiper excessive sweep",m=>m["wipers"]["sweepDegrees"]=new JArray(101,70),"range");
            Bad("wiper missing sweep",m=>m["wipers"]["sweepDegrees"]=new JArray(70),"Two wiper");
            var missing=(JObject)geometry.DeepClone();((JArray)missing["parts"]).Last.Remove();Reject(()=>Parse(manifest,missing),"missing authored mesh blade","metadata");
            foreach(string duplicate in new[]{"wipers","pivots"})
            {
                string original=Encoding.UTF8.GetString(Bytes(manifest));string injected=duplicate=="wipers"?original.Insert(1,"\"wipers\":"+manifest["wipers"].ToString(Formatting.None)+","):original.Replace("\"pivots\":","\"pivots\":[],\"pivots\":");
                Reject(()=>Parse(manifest,null,files=>files["manifest.json"]=Encoding.UTF8.GetBytes(injected)),"duplicate authored "+duplicate);
            }
            var badFinishes=new Action<JObject>[]
            {
                f=>f["kind"]="unknown",f=>f["transmission"]=new JArray(0,1,1),f=>f["transmission"]=new JArray(1,1),
                f=>f["opticalThickness"]=-1,f=>f["flakeScalePerMetre"]=1000000,f=>f["clearcoat"]=2
            };
            foreach(var mutate in badFinishes)
            {var g=(JObject)geometry.DeepClone();mutate((JObject)g["materials"][0]["finish"]);Reject(()=>Parse(manifest,g),"invalid layered finish");}
            Bad("orphan engine WAVs",m=>m.Remove("engineAudio"),"Orphaned");
            Bad("audio hash mismatch",m=>m["engineAudio"]["exhaust"]["sha256"]=new string('0',64),"checksum");
            Bad("external audio filename",m=>m["engineAudio"]["mechanical"]["file"]="../outside.wav","loop file");
            Bad("unmarked RPM estimate",m=>((JObject)m["engineAudio"]["exhaust"]).Remove("referenceRpmEstimated"),"estimated");
            Reject(()=>Parse(manifest,null,files=>files.Remove("engine-mechanical.wav")),"missing audio payload","both WAV");
            Reject(()=>Parse(manifest,null,files=>{files["engine-exhaust.wav"]=Alter(wav,20,3,0);var m=JObject.Parse(Encoding.UTF8.GetString(files["manifest.json"]));m["engineAudio"]["exhaust"]["sha256"]=VehiclePackFormat.Digest(files["engine-exhaust.wav"]);files["manifest.json"]=Bytes(m);}),"malformed embedded PCM after valid checksum","PCM16");
        }
        static string Argument(string key)
        {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);if(i<0)return null;if(i+1>=args.Length||args[i+1].StartsWith("-",StringComparison.Ordinal))throw new ArgumentException("Missing value for "+key);return args[i+1];}
        static void Selected()
        {
            string path=Argument("-earthUnscriptedVehiclePackFixture"),expected=Argument("-earthUnscriptedVehiclePackFixtureSha256");
            if(path==null){Check(expected==null,"expected hash requires selected fixture");Debug.Log("VEHICLE PACK EXTENSION selected fixture not supplied; synthetic admission and built-in compatibility only");return;}
            string hash=VehiclePackFormat.Digest(File.ReadAllBytes(path));if(expected!=null)Check(hash.Equals(expected,StringComparison.OrdinalIgnoreCase),"selected archive matches expected SHA256");
            var pack=VehiclePackFormat.Read(path);Check(pack.ArchiveHash==hash,"selected fixture parsed from exact hashed bytes");Check(pack.Settings.Price==50000,"selected truck costs 50000 credits");
            Check(pack.Wipers!=null&&pack.Wipers.Pivots.Length==2&&pack.Wipers.SweepDegrees.All(s=>Math.Abs(s)>=10),"selected truck authored wipers");
            Check(pack.EngineAudio!=null&&pack.EngineAudio.Exhaust.Samples.Length>0&&pack.EngineAudio.Mechanical.Samples.Length>0,"selected truck embedded PCM loops");
            Check(pack.Materials.Any(m=>m.Finish!=null),"selected truck layered finish");
            Check(pack.ArchiveHash==VehiclePackFormat.Digest(File.ReadAllBytes(path)),"selected archive unmodified by parsing");
            Debug.Log("VEHICLE PACK EXTENSION selected="+Path.GetFileName(path)+" id="+pack.Settings.Id+" sha256="+hash);
        }
        public static void Run()
        {
            checks=serial=0;int initialCars=Core.ImportedVehicleCatalogue.Entries.Count;var initialCatalogue=Core.GarageRules.Catalogue;string directory=Path.Combine(Path.GetTempPath(),"EarthUnscriptedVehiclePackVerification-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try
            {
                Pcm();JObject legacy=null;
                foreach(string filename in new[]{"Hekla-Formula-Offroad-2.0.0.eucar","VW-Splittie-2.0.0.eucar"})
                {
                    string path=Path.Combine(Application.streamingAssetsPath,"VehiclePacks",filename);byte[] bytes=File.ReadAllBytes(path);string hash=VehiclePackFormat.Digest(bytes);
                    var first=VehiclePackFormat.Read(path);string fingerprint=Fingerprint(first);var second=VehiclePackFormat.Read(path);
                    Check(first.EngineAudio==null&&first.Wipers==null&&first.Materials.All(m=>m.Finish==null),filename+" retains legacy synthesis/wiper/paint admission");
                    Check(fingerprint==Fingerprint(second),filename+" parser result unchanged across reads");
                    Check(first.ArchiveHash==hash&&hash==VehiclePackFormat.Digest(File.ReadAllBytes(path)),filename+" archive unchanged");
                    var original=JObject.Parse(Encoding.UTF8.GetString(ReadFiles(path)["manifest.json"]));
                    Check(first.Settings.Id==(string)original["id"]&&first.Settings.Version==(string)original["version"]&&first.Settings.Price==(int)original["priceCredits"],filename+" legacy identity/version/cost retained");
                    Check(first.Settings.WheelRadius==(float)original["physics"]["wheelRadius"]&&first.Settings.CockpitY==(float)original["physics"]["cockpitY"],filename+" legacy physics/cockpit retained");
                    if(legacy==null)legacy=original;
                }
                byte[] wav=Wav(44100,11025);Extensions(directory,Manifest(legacy,wav),Geometry(),wav);Selected();
                Check(initialCars==Core.ImportedVehicleCatalogue.Entries.Count&&ReferenceEquals(initialCatalogue,Core.GarageRules.Catalogue),"parse-only checks never register or activate cars");
                Debug.Log("VEHICLE PACK EXTENSION PASS "+checks+"; actual game parser/PCM, parse-only, temporary archives cleaned");
            }
            finally{Directory.Delete(directory,true);}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
