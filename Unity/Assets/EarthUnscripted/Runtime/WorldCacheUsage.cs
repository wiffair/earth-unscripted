using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace EarthUnscripted
{
    // File inventories run on a worker; the HUD only reads an immutable result.
    public sealed class WorldCacheUsage
    {
        public sealed class Snapshot
        {
            public long prepared,finished,sources,terrain,free;
            public double preparedLimit=5,finishedLimit=5,sourceLimit=5;
            public string Summary=>$"Cache GB: areas {prepared/1e9:F2}/{preparedLimit:0.#} · scenery {finished/1e9:F2}/{finishedLimit:0.#} · sources {sources/1e9:F2}/{sourceLimit:0.#}";
        }
        Task<Snapshot> pending;double next;public Snapshot Latest {get;private set;}
        public void Refresh(string root,double now)
        {
            if(pending!=null&&pending.IsCompleted)
            {if(pending.Status==TaskStatus.RanToCompletion)Latest=pending.Result;pending=null;}
            if(pending!=null||now<next)return;
            next=now+30;pending=Task.Run(()=>Measure(root));
        }
        public static Snapshot Measure(string root)
        {
            var result=new Snapshot {prepared=Bytes(Path.Combine(root,"Prepared")),finished=Bytes(Path.Combine(root,"FinishedGeometry")),sources=Bytes(Path.Combine(root,"Raw")),terrain=Bytes(Path.Combine(root,"FarTerrain"))};
            try{result.free=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace;}catch(IOException){}catch(UnauthorizedAccessException){}
            try
            {
                var settings=JObject.Parse(File.ReadAllText(Path.Combine(root,"storage-settings.json")));
                double Limit(string name,double minimum)
                {var token=settings[name];if(token==null||!(token.Type==JTokenType.Integer||token.Type==JTokenType.Float))return 5;double value=(double)token;return double.IsNaN(value)||double.IsInfinity(value)?5:Math.Max(minimum,Math.Min(50,value));}
                result.preparedLimit=Limit("preparedGigabytes",.5);result.finishedLimit=Limit("finishedGigabytes",0);result.sourceLimit=Limit("sourceGigabytes",.5);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}catch(Newtonsoft.Json.JsonException){}
            return result;
        }
        static long Bytes(string root)
        {
            long total=0;if(!Directory.Exists(root))return total;
            try{foreach(string path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
                try{total+=new FileInfo(path).Length;}catch(IOException){}catch(UnauthorizedAccessException){}}
            catch(IOException){}catch(UnauthorizedAccessException){}
            return total;
        }
    }
}
