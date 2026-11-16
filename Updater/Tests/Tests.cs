using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Updates;

namespace EarthUnscripted.UpdaterTests
{
    internal static class Runner
    {
        static int checks;
        static readonly RSA Signing=RSA.Create(3072);
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Reject(Action action,string name){try{action();}catch{checks++;Console.WriteLine("PASS "+name);return;}throw new Exception("Accepted invalid input: "+name);}
        static byte[] Bytes(string text)=>Encoding.UTF8.GetBytes(text);
        static JObject Artifact(string file,string target="macOS-arm64")=>new JObject{{"target",target},{"url","https://github.com/wiffair/earth-unscripted/releases/download/v0.24.0-beta.4/test.zip"},{"bytes",new FileInfo(file).Length},{"sha256",UpdateProtocol.HashFile(file)},{"unpackedBytes",100000000}};
        static JObject Payload(string zip)
        {return new JObject{{"format","EarthUnscriptedUpdates"},{"schemaVersion",1},{"sequence",2},{"issuedUtc",DateTime.UtcNow.AddMinutes(-1).ToString("O")},{"expiresUtc",DateTime.UtcNow.AddDays(30).ToString("O")},{"game",new JObject{{"version","0.24.0-beta.4"},{"channel","beta"},{"downloads",new JArray(Artifact(zip),Artifact(zip,"Windows-x64"))}}},{"packs",new JArray()}};}
        static byte[] Envelope(JObject payload)
        {byte[] bytes=Bytes(payload.ToString());return Bytes(new JObject{{"format","EarthUnscriptedSignedUpdates"},{"schemaVersion",1},{"keyId","test-key"},{"payload",Convert.ToBase64String(bytes)},{"signature",Convert.ToBase64String(Signing.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))}}.ToString());}
        static JObject Verify(byte[] bytes,long sequence)=>UpdateProtocol.ReadSigned(bytes,Signing.ExportParameters(false),"test-key",sequence);
        static int Main(string[] args)
        {
            if(args.SequenceEqual(new[]{"--check"}))
            {string path=Environment.ProcessPath;return File.Exists(Path.Combine(Path.GetDirectoryName(path),"fail-on-installed-check"))&&!path.Contains(".earth-unscripted-stage-")?17:0;}
            if(args.Length==2&&args[0]=="--delta-proof")
            {
                string folder=Path.GetFullPath(args[1]);var artifact=UpdateProtocol.Json(File.ReadAllBytes(Path.Combine(folder,"artifact.json")));var descriptor=UpdateProtocol.Json(File.ReadAllBytes(Path.Combine(folder,"recipe-artifact.json")));
                var delta=new RadioDelta();string output=Path.Combine(folder,"reconstructed.euradio");delta.Reconstruct(artifact,descriptor,output,Path.Combine(folder,"cache"),new[]{Path.Combine(folder,"old.euradio")},Console.WriteLine,CancellationToken.None).GetAwaiter().GetResult();
                Check(delta.DownloadedBytes==0&&delta.ReusedBytes>(long)artifact["bytes"]*.8,"playlist-only update reuses audio without network downloads");UpdateProtocol.VerifyFile(output,artifact);Console.WriteLine("Reused "+delta.ReusedBytes+" bytes; reconstructed exact target pack.");return 0;
            }
            if(args.Length==2&&args[0]=="--verify-feed")
            {var catalogue=UpdateProtocol.ReadSigned(File.ReadAllBytes(args[1]),0);Console.WriteLine("Verified production signature: "+catalogue["game"]["version"]+", sequence "+catalogue["sequence"]);return 0;}
            if(args.SequenceEqual(new[]{"--fetch-feed"}))
            {var response=UpdateTransport.Feed("",CancellationToken.None).GetAwaiter().GetResult();var catalogue=UpdateProtocol.ReadSigned(response.Item1,0);var cached=UpdateTransport.Feed(response.Item2,CancellationToken.None).GetAwaiter().GetResult();Check(cached.Item1==null,"production feed honours conditional requests");Console.WriteLine("Live signed feed verified: "+catalogue["game"]["version"]);return 0;}
            if(args.Length==2&&args[0]=="--check-archive")
            {UpdateProtocol.CheckArchive(args[1],8L*1024*1024*1024,OperatingSystem.IsMacOS());Console.WriteLine("Archive paths and expansion limits passed.");return 0;}
            string temp=Path.Combine(Path.GetTempPath(),"EarthUnscriptedUpdateTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try
            {
                Check(UpdateProtocol.Json(Bytes("{\"issuedUtc\":\"2026-11-16T18:00:00Z\"}"))["issuedUtc"].Type==JTokenType.String,"JSON retains UTC timestamps as strings for invariant parsing");
                Reject(()=>UpdateProtocol.Json(Bytes("{}{}")),"trailing JSON metadata rejected");
                Check(UpdateProtocol.CompareVersion("0.24.0-beta.10","0.24.0-beta.3")>0,"semantic beta ordering");
                Check(UpdateProtocol.CompareVersion("1.0.0","1.0.0-beta.10")>0,"stable follows prerelease");
                foreach(string value in new[]{"01.2.3","1.2","../../x","1.0.0-beta.0"})Reject(()=>UpdateProtocol.VersionParts(value),"version guard "+value);
                foreach(string value in new[]{"../x","/x","a/../../x","a\\x","CON.txt","a/b.","a//b"})Reject(()=>UpdateProtocol.SafeRelative(value),"path guard "+value);
                foreach(string value in new[]{"http://github.com/wiffair/earth-unscripted/releases/download/x/y","https://evil.example/x","https://github.com/other/repo/releases/download/x/y"})Reject(()=>UpdateProtocol.ApprovedUrl(value),"download host guard");
                string zip=Path.Combine(temp,"small.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)){using var w=new StreamWriter(z.CreateEntry("ok.txt").Open());w.Write("good");}
                var payload=Payload(zip);byte[] envelope=Envelope(payload);Check((long)Verify(envelope,2)["sequence"]==2,"signed catalogue accepted");
                var damaged=JObject.Parse(Encoding.UTF8.GetString(envelope));damaged["payload"]=Convert.ToBase64String(Bytes(payload.ToString()+" "));Reject(()=>Verify(Bytes(damaged.ToString()),0),"tampered signed payload");
                Reject(()=>Verify(envelope,3),"catalogue replay rejected");var expired=(JObject)payload.DeepClone();expired["expiresUtc"]=DateTime.UtcNow.AddDays(-1).ToString("O");Reject(()=>Verify(Envelope(expired),0),"expired catalogue rejected");
                Reject(()=>UpdateProtocol.VerifyFile(zip,new JObject{{"bytes",1},{"sha256",new string('a',64)}}),"artifact integrity rejected");
                string traversal=Path.Combine(temp,"traversal.zip");using(var z=ZipFile.Open(traversal,ZipArchiveMode.Create)){z.CreateEntry("../escape.txt");}Reject(()=>UpdateProtocol.CheckArchive(traversal,1000,false),"archive traversal rejected");
                string duplicate=Path.Combine(temp,"duplicate.zip");using(var z=ZipFile.Open(duplicate,ZipArchiveMode.Create)){z.CreateEntry("a.txt");z.CreateEntry("A.txt");}Reject(()=>UpdateProtocol.CheckArchive(duplicate,1000,false),"case-conflicting archive rejected");
                string link=Path.Combine(temp,"link.zip");using(var z=ZipFile.Open(link,ZipArchiveMode.Create)){var e=z.CreateEntry("link");e.ExternalAttributes=unchecked((int)0xa1ff0000);using var w=new StreamWriter(e.Open());w.Write("../../outside");}Reject(()=>UpdateProtocol.CheckArchive(link,1000,true),"escaping symbolic link rejected");
                InstallCase(temp,false);InstallCase(temp,true);
                Console.WriteLine("All "+checks+" update checks passed.");return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally{try{Directory.Delete(temp,true);}catch{}}
        }
        static void InstallCase(string parent,bool fail)
        {
            bool mac=OperatingSystem.IsMacOS();string platform=mac?"macOS-arm64":"Windows-x64";
            string root=Path.Combine(parent,fail?"rollback case":"successful case");Directory.CreateDirectory(root);
            string target=Path.Combine(root,mac?"Earth Unscripted Beta.app":"Earth Unscripted");Directory.CreateDirectory(target);
            string current=Path.Combine(target,mac?"Contents/Resources":"");Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(current,"InstallManifest.json"),new JObject{{"product","earth-unscripted"},{"target",platform},{"version","0.24.0-beta.3"}}.ToString());File.WriteAllText(Path.Combine(target,"old-kept.txt"),"owned save outside app remains untouched");
            string source=Path.Combine(root,"source","Earth Unscripted");if(mac)source=Path.Combine(source,"Earth Unscripted Beta.app");Directory.CreateDirectory(source);
            string resource=Path.Combine(source,mac?"Contents/Resources":"");Directory.CreateDirectory(resource);
            File.WriteAllText(Path.Combine(resource,"InstallManifest.json"),new JObject{{"product","earth-unscripted"},{"target",platform},{"version","0.24.0-beta.4"}}.ToString());
            foreach(string mode in new[]{"Standard","HD"})
            {
                string data=Path.Combine(source,mac?"Contents/Resources/Players/"+mode+".app/Contents/Resources/Data":"Players/"+mode+"/Earth Unscripted Beta"+(mode=="HD"?" HD":"")+"_Data");
                Directory.CreateDirectory(Path.Combine(data,"StreamingAssets"));Directory.CreateDirectory(Path.Combine(data,"Managed"));File.WriteAllText(Path.Combine(data,"Managed/Assembly-CSharp.dll"),"same fixture assembly");
                File.WriteAllText(Path.Combine(data,"StreamingAssets/SharedGameSource.json"),new JObject{{"version","0.24.0-beta.4"},{"source",new string('a',64)},{"assembly",UpdateProtocol.HashFile(Path.Combine(data,"Managed/Assembly-CSharp.dll"))}}.ToString());
            }
            string executable=Path.Combine(source,mac?"Contents/MacOS/ChooseGraphics":"Earth Unscripted.exe");Directory.CreateDirectory(Path.GetDirectoryName(executable));File.Copy(Environment.ProcessPath,executable);
            if(mac)File.SetUnixFileMode(executable,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);
            if(fail)File.WriteAllText(Path.Combine(Path.GetDirectoryName(executable),"fail-on-installed-check"),"fixture");
            string zip=Path.Combine(root,"game.zip");ZipFile.CreateFromDirectory(Path.Combine(root,"source"),zip,CompressionLevel.Fastest,false);
            string signed=Path.Combine(root,"signed.json");File.WriteAllBytes(signed,Envelope(Payload(zip)));
            string job=Path.Combine(root,"job.json");File.WriteAllText(job,new JObject{{"targetPath",target},{"archivePath",zip},{"envelopePath",signed},{"minimumSequence",2},{"parentPid",0},{"reopen",false}}.ToString());
            int code=global::Program.Run(new[]{"--apply",job},Verify,_=>{});
            if(fail){Check(code!=0&&File.Exists(Path.Combine(target,"old-kept.txt")),"failed post-install probe restores old installation");}
            else{Check(code==0&&UpdateProtocol.Json(File.ReadAllBytes(Path.Combine(target,mac?"Contents/Resources/InstallManifest.json":"InstallManifest.json")))["version"].ToString()=="0.24.0-beta.4","complete pair installed and probed");Check(Directory.GetDirectories(root,"*.rollback-*").Length==1,"previous game retained for recovery");}
        }
    }
}
