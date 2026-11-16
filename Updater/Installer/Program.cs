using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using EarthUnscripted.Updates;
using Newtonsoft.Json.Linq;

internal static class Program
{
    [DllImport("libc",SetLastError=true)] static extern int renamex_np(string from,string to,uint flags);
    static int Main(string[] args) => Run(args);
    internal static int Run(string[] args,Func<byte[],long,JObject> verifier=null)
    {
        verifier=verifier??UpdateProtocol.ReadSigned;
        string result=null;
        try
        {
            if(args.Length!=2||args[0]!="--apply")throw new InvalidDataException("Use --apply with a prepared update job.");
            string jobPath=Path.GetFullPath(args[1]);var job=UpdateProtocol.Json(File.ReadAllBytes(jobPath));
            result=Path.Combine(Path.GetDirectoryName(jobPath),"install-result.json");
            var catalogue=verifier(File.ReadAllBytes((string)job["envelopePath"]),(long?)job["minimumSequence"]??0);
            string target=Path.GetFullPath((string)job["targetPath"]),zipPath=Path.GetFullPath((string)job["archivePath"]);
            bool mac=OperatingSystem.IsMacOS();string platform=mac?"macOS-arm64":"Windows-x64";
            var game=(JObject)catalogue["game"];var artifact=((JArray)game["downloads"]).OfType<JObject>().Single(item=>(string)item["target"]==platform);
            string version=(string)game["version"];
            UpdateProtocol.VerifyFile(zipPath,artifact);
            UpdateProtocol.CheckArchive(zipPath,(long)artifact["unpackedBytes"],mac);
            RequireDirectory(target);var old=InstallInfo(target,mac);
            UpdateProtocol.Require((string)old["product"]=="earth-unscripted"&&(string)old["target"]==platform,"This is not an Earth Unscripted installation.");
            UpdateProtocol.Require(UpdateProtocol.CompareVersion(version,(string)old["version"])>0,"The update does not increase the installed version.");
            int pid=(int?)job["parentPid"]??0;
            if(pid>0)
            {
                try
                {
                    using var process=Process.GetProcessById(pid);
                    long expected=(long?)job["parentStartTicks"]??0;
                    if(Math.Abs(process.StartTime.ToUniversalTime().Ticks-expected)<TimeSpan.TicksPerSecond*2)
                        UpdateProtocol.Require(process.WaitForExit(20*60*1000),"The game is still running; update cancelled.");
                }
                catch(ArgumentException) { }
            }
            string parent=Path.GetDirectoryName(target),token=Guid.NewGuid().ToString("N");
            string stage=Path.Combine(parent,".earth-unscripted-stage-"+token),backup=target+".rollback-"+token;
            Directory.CreateDirectory(stage);
            string packageRoot=mac?"Earth Unscripted/Earth Unscripted Beta.app":"Earth Unscripted";
            string candidate=UpdateProtocol.Contained(stage,packageRoot);
            var journal=new JObject{{"target",target},{"backup",backup},{"stage",stage},{"candidate",candidate},{"oldVersion",old["version"]},{"version",version},{"state","preparing"}};
            string journalPath=Path.Combine(Path.GetDirectoryName(jobPath),"install-transaction.json");
            void Record(string state){journal["state"]=state;UpdateProtocol.AtomicText(journalPath,journal.ToString());}
            bool movedOld=false,movedNew=false,swapped=false;
            try
            {
                Extract(zipPath,stage,mac);
                ValidateCandidate(candidate,platform,version,mac);Probe(candidate,mac);
                Record("prepared");
                if(mac)
                {
                    // APFS/HFS+ exchange preserves the app path even across power loss.
                    if(renamex_np(target,candidate,2)!=0)throw new IOException("The app could not be exchanged atomically: "+Marshal.GetLastWin32Error());
                    swapped=true;movedOld=true;movedNew=true;Record("exchanged");
                    Directory.Move(candidate,backup);Record("backup-retained");
                }
                else
                {
                    // Windows directory moves are journalled and reversed on any failure.
                    Directory.Move(target,backup);movedOld=true;Record("old-moved");
                    Directory.Move(candidate,target);movedNew=true;Record("new-moved");
                }
                Probe(target,mac);Record("verified");
                UpdateProtocol.AtomicText(result,new JObject{{"ok",true},{"version",version},{"backup",backup},{"message","Update installed. Your saves and packs were preserved."}}.ToString());
                try{Directory.Delete(stage,true);}catch(IOException){}
                if((bool?)job["reopen"]!=false)
                {
                    var start=mac?new ProcessStartInfo("/usr/bin/open"):new ProcessStartInfo(Path.Combine(target,"Earth Unscripted.exe"));
                    start.UseShellExecute=false;if(mac)start.ArgumentList.Add(target);Process.Start(start);
                }
                return 0;
            }
            catch
            {
                if(swapped&&Directory.Exists(candidate)&&!Directory.Exists(backup))
                {if(renamex_np(target,candidate,2)!=0)throw new IOException("Automatic rollback failed; the previous app remains at "+candidate);}
                else if(movedOld&&Directory.Exists(backup))
                {
                    if(movedNew&&Directory.Exists(target))Directory.Move(target,target+".failed-"+token);
                    if(!Directory.Exists(target))Directory.Move(backup,target);
                }
                Record("rolled-back");throw;
            }
        }
        catch(Exception error)
        {
            if(result!=null)try{UpdateProtocol.AtomicText(result,new JObject{{"ok",false},{"message",error.Message}}.ToString());}catch{}
            Console.Error.WriteLine("Update not installed: "+error.Message);return 1;
        }
    }
    static void RequireDirectory(string path)
    {UpdateProtocol.Require(Directory.Exists(path)&&new DirectoryInfo(path).LinkTarget==null,"The installation folder is missing or is a symbolic link.");}
    static JObject InstallInfo(string root,bool mac)=>UpdateProtocol.Json(File.ReadAllBytes(Path.Combine(root,mac?"Contents/Resources/InstallManifest.json":"InstallManifest.json")));
    static void ValidateCandidate(string path,string platform,string version,bool mac)
    {
        RequireDirectory(path);var info=InstallInfo(path,mac);
        UpdateProtocol.Require((string)info["product"]=="earth-unscripted"&&(string)info["version"]==version&&(string)info["target"]==platform,"Candidate installation identity does not match the signed release.");
        string source=null;
        foreach(string mode in new[]{"Standard","HD"})
        {
            string player=Path.Combine(path,mac?"Contents/Resources/Players/"+mode+".app/Contents/Resources/Data":"Players/"+mode+"/Earth Unscripted Beta"+(mode=="HD"?" HD":"")+"_Data");
            var shared=UpdateProtocol.Json(File.ReadAllBytes(Path.Combine(player,"StreamingAssets/SharedGameSource.json")));
            string current=(string)shared["source"];UpdateProtocol.Require((string)shared["version"]==version&&UpdateProtocol.HashText(current),"Player version/source mismatch.");
            if(source==null)source=current;else UpdateProtocol.Require(current==source,"Standard and HD are from different updates.");
            UpdateProtocol.Require(UpdateProtocol.HashFile(Path.Combine(player,"Managed/Assembly-CSharp.dll"))==(string)shared["assembly"],"Player assembly integrity failed.");
        }
    }
    static void Probe(string path,bool mac)
    {
        string executable=Path.Combine(path,mac?"Contents/MacOS/ChooseGraphics":"Earth Unscripted.exe");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--check");
        using var process=Process.Start(start);UpdateProtocol.Require(process.WaitForExit(30000)&&process.ExitCode==0,"The new launcher failed its installation check.");
    }
    static void Extract(string archive,string stage,bool mac)
    {
        using var zip=ZipFile.OpenRead(archive);var links=new List<(string,string)>();
        foreach(var entry in zip.Entries)
        {
            string output=UpdateProtocol.Contained(stage,entry.FullName);
            if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(output);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if(((entry.ExternalAttributes>>16)&0xf000)==0xa000)
            {using var reader=new StreamReader(entry.Open());links.Add((output,reader.ReadToEnd()));continue;}
            using(var input=entry.Open())using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(file);file.Flush(true);UpdateProtocol.Require(file.Length==entry.Length,"Incomplete extracted file.");}
            if(OperatingSystem.IsMacOS()){int mode=(entry.ExternalAttributes>>16)&0x1ff;File.SetUnixFileMode(output,(UnixFileMode)(mode==0?0x1a4:mode));}
        }
        foreach(var (output,target) in links)File.CreateSymbolicLink(output,target);
    }
}
