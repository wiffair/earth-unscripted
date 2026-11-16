using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    // Explicit offline authoring tool. Never opens an installed country DB writable,
    // prepares profiles, changes bundled Resources, or changes non-rail world data.
    public static class BundledRailwayPreparation
    {
        static string Hash(byte[] data){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
        static string NonRail(string json){var obj=JObject.Parse(json);foreach(string key in new[]{"railways","railwayProfilesPrepared","railwaySourceIdentity","railwaySourceComplete"})obj.Remove(key);return obj.ToString(Newtonsoft.Json.Formatting.None);}
        static string Argument(string name){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);if(i<0||i+1>=args.Length||args[i+1].StartsWith("-"))throw new ArgumentException("Explicit "+name+" value required");return args[i+1];}
        public static void Run()
        {
            try{string output=PrepareCandidate(Argument("-earthUnscriptedRailCountry"),"Assets/EarthUnscripted/Resources/Geo/dee-valley.json","Builds/Verification/BundledRailwayUpgrade");Debug.Log("BUNDLED RAIL CANDIDATE "+output);EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static string PrepareCandidate(string countryFolder,string bundledPath,string outputRoot)
        {
            string database=Path.GetFullPath(Path.Combine(countryFolder,"map.sqlite"));if(!File.Exists(database))throw new FileNotFoundException("Explicit country map.sqlite missing",database);
            // Immutable mode cannot see WAL pages. Require a separately checkpointed,
            // idle source; this tool never checkpoints or creates WAL/SHM files itself.
            if(File.Exists(database+"-wal")&&new FileInfo(database+"-wal").Length>0)throw new InvalidDataException("Country has uncheckpointed WAL; provide a closed/checkpointed source copy. Source not modified.");
            var info=new FileInfo(database);long length=info.Length;DateTime modified=info.LastWriteTimeUtc;
            byte[] original=File.ReadAllBytes(bundledPath);string originalText=Encoding.UTF8.GetString(original),originalHash=Hash(original);
            var baseline=JsonUtility.FromJson<GeoRegion>(originalText);string normalized=JsonUtility.ToJson(baseline);var region=JsonUtility.FromJson<GeoRegion>(normalized);
            if(region.circuit||region.width<=0||region.depth<=0||region.width>20000||region.depth>20000||region.heights==null||region.heights.Length!=region.nx*region.nz)throw new InvalidDataException("Unsupported bundled region extent/terrain");
            double cos=Math.Cos(region.latitude*Math.PI/180),west=region.longitude+region.minX/(111319.4908*cos),east=region.longitude+(region.minX+region.width)/(111319.4908*cos),south=region.latitude+region.minZ/111319.4908,north=region.latitude+(region.minZ+region.depth)/111319.4908;
            if(cos<=0||double.IsNaN(west)||double.IsNaN(south))throw new InvalidDataException("Invalid bundled projection");
            var rails=new List<GeoRailway>();var sourceRecords=new JArray();int total=0,rawRefs=0,rawWays=0,rectFragments=0;string sourceHash;
            using(var db=new ReadOnlyDatabase(database))
            {
                var meta=new Dictionary<string,string>();using(var q=db.Query("SELECT key,value FROM metadata WHERE key IN ('railway_schema','railway_complete','railway_source_sha256')"))while(q.Step())meta.Add(q.Text(0),q.Text(1));
                if(!meta.TryGetValue("railway_schema",out var schema)||schema!="1"||!meta.TryGetValue("railway_complete",out var complete)||complete!="1"||!meta.TryGetValue("railway_source_sha256",out sourceHash)||!Regex.IsMatch(sourceHash,"^[a-f0-9]{64}$"))throw new InvalidDataException("Complete railway schema1 with source SHA256 is required");
                using(var q=db.Query("SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('railways','rail_bounds')")){if(!q.Step()||q.Integer(0)!=2)throw new InvalidDataException("Complete railway tables missing");}
                using var rows=db.Query("SELECT r.id,r.tags,r.geometry FROM rail_bounds b JOIN railways r ON r.id=b.id WHERE b.minLon<=?1 AND b.maxLon>=?2 AND b.minLat<=?3 AND b.maxLat>=?4 ORDER BY r.id LIMIT 2049");
                rows.Bind(1,east);rows.Bind(2,west);rows.Bind(3,north);rows.Bind(4,south);
                while(rows.Step())
                {
                    if(++rawWays>2048)throw new InvalidDataException("Full bundled extent exceeds2048 source railway ways; no truncation permitted");
                    long id=rows.Integer(0);string tagsText=rows.Text(1);byte[] blob=rows.Bytes(2);var tags=JObject.Parse(tagsText);
                    if(blob.Length<4)throw new InvalidDataException("Truncated source geometry");
                    using var reader=new BinaryReader(new MemoryStream(blob));int count=reader.ReadInt32();if(count<2||count>50000||blob.Length!=4L+count*16L)throw new InvalidDataException("Invalid source way node count/bytes");
                    rawRefs+=count;if(rawRefs>200000)throw new InvalidDataException("Full bundled extent exceeds200000 raw node references; no truncation permitted");
                    var points=new List<Vector3>(count);var nodes=new JArray();
                    for(int i=0;i<count;i++)
                    {
                        long node=reader.ReadInt64();double lat=reader.ReadInt32()/1e7,lon=reader.ReadInt32()/1e7;
                        if(node<=0||lat< -90||lat>90||lon< -180||lon>180)throw new InvalidDataException("Invalid raw source node reference/coordinate; gaps cannot be fabricated");
                        nodes.Add(node);points.Add(new Vector3((float)((lon-region.longitude)*111319.4908*cos),0,(float)((lat-region.latitude)*111319.4908)));
                    }
                    int before=rails.Count;rectFragments+=AppendRectangular(rails,region,tags,id.ToString(CultureInfo.InvariantCulture),nodes,points,ref total);
                    sourceRecords.Add(new JObject{{"id",id},{"rawNodeReferences",count},{"tagsSHA256",Hash(Encoding.UTF8.GetBytes(tagsText))},{"geometrySHA256",Hash(blob)},{"outputFragments",rails.Count-before}});
                }
            }
            info.Refresh();if(info.Length!=length||info.LastWriteTimeUtc!=modified||(File.Exists(database+"-wal")&&new FileInfo(database+"-wal").Length>0))throw new IOException("Country source changed during offline read; candidate not emitted");
            region.railways=rails.ToArray();region.railwayProfilesPrepared=false;region.railwaySourceIdentity="railways1|complete|"+sourceHash;region.railwaySourceComplete=true;
            string candidate=JsonUtility.ToJson(region);string preserved=NonRail(normalized);if(!string.Equals(preserved,NonRail(candidate),StringComparison.Ordinal))throw new InvalidDataException("Rail-only authoring changed non-rail serialized data");
            if(Hash(File.ReadAllBytes(bundledPath))!=originalHash)throw new IOException("Original bundled source changed during preparation");
            string folder=Path.GetFullPath(Path.Combine(outputRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff",CultureInfo.InvariantCulture)+"-"+originalHash.Substring(0,12)));Directory.CreateDirectory(folder);
            byte[] result=Encoding.UTF8.GetBytes(candidate);File.WriteAllBytes(Path.Combine(folder,"original-bundled.json"),original);File.WriteAllBytes(Path.Combine(folder,region.id+"-rail-candidate.json"),result);
            var receipt=new JObject{{"format","EarthUnscriptedBundledRailCandidate1"},{"activation",false},{"countryDatabase",database},{"countryDatabaseBytes",length},{"countryDatabaseModifiedUTC",modified.ToString("O")},{"databaseReadMode","SQLITE_OPEN_READONLY|URI immutable=1; nonempty WAL refused; no source writes"},{"railwaySourceSHA256",sourceHash},{"sourceBundledPath",Path.GetFullPath(bundledPath)},{"sourceBundledSHA256",originalHash},{"candidateSHA256",Hash(result)},{"nonRailSerializedSHA256",Hash(Encoding.UTF8.GetBytes(preserved))},{"nonRailSerializedUnchanged",true},{"worldBounds",new JArray(region.minX,region.minZ,region.minX+region.width,region.minZ+region.depth)},{"geographicBounds",new JArray(west,south,east,north)},{"sourceWays",rawWays},{"rawNodeReferences",rawRefs},{"rectangularSourceFragments",rectFragments},{"outputWays",rails.Count},{"outputPoints",rails.Sum(r=>r.points.Length)},{"railwayProfilesPrepared",false},{"sourceRowsSHA256",Hash(Encoding.UTF8.GetBytes(sourceRecords.ToString(Newtonsoft.Json.Formatting.None)))},{"sourceRows",sourceRecords},{"gapPolicy","Retains stored source node alignment. Clip fragments have deterministic synthetic boundary nodes. Does not infer missing refs/unknown source segments; complete schema denotes imported source capability, not a claim of surveyed geometry."},{"historyPolicy","Original bytes retained alongside candidate; datasetVersion/datasetSource/attribution and all non-rail serialized values unchanged. No Resources overwrite."}};
            File.WriteAllText(Path.Combine(folder,"receipt.json"),receipt.ToString(Newtonsoft.Json.Formatting.Indented));return folder;
        }
        static int AppendRectangular(List<GeoRailway> output,GeoRegion region,JObject tags,string id,JArray sourceIds,List<Vector3> source,ref int total)
        {
            var points=new List<Vector3>();var ids=new JArray();int fragments=0,budget=total;
            float half=Mathf.Max(Mathf.Abs(region.minX),Mathf.Abs(region.minX+region.width),Mathf.Abs(region.minZ),Mathf.Abs(region.minZ+region.depth))+.01f;
            void Flush(){if(points.Count>=2){RailwayImport.Append(output,region,tags,id+"-rect-"+fragments,ids,points,half,ref budget);fragments++;}points.Clear();ids.Clear();}
            for(int i=0;i<source.Count-1;i++)
            {
                var a=source[i];var b=source[i+1];float lo=0,hi=1;
                bool Edge(float p,float q){if(Mathf.Abs(p)<.000001f)return q>=0;float t=q/p;if(p<0){if(t>hi)return false;lo=Mathf.Max(lo,t);}else{if(t<lo)return false;hi=Mathf.Min(hi,t);}return true;}
                if(!Edge(a.x-b.x,a.x-region.minX)||!Edge(b.x-a.x,region.minX+region.width-a.x)||!Edge(a.z-b.z,a.z-region.minZ)||!Edge(b.z-a.z,region.minZ+region.depth-a.z)||hi-lo<.000001f){Flush();continue;}
                string first=lo<=.000001f?sourceIds[i].ToString():"railrect-"+id+"-"+i+"-a",last=hi>=.999999f?sourceIds[i+1].ToString():"railrect-"+id+"-"+i+"-b";
                if(ids.Count>0&&ids[ids.Count-1].ToString()!=first)Flush();if(points.Count==0){points.Add(Vector3.Lerp(a,b,lo));ids.Add(first);}points.Add(Vector3.Lerp(a,b,hi));ids.Add(last);if(hi<.999999f)Flush();
            }
            Flush();total=budget;return fragments;
        }
        sealed class ReadOnlyDatabase:IDisposable
        {
#if UNITY_EDITOR_WIN
            const string Library="sqlite3";
#else
            const string Library="/usr/lib/libsqlite3.dylib";
#endif
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close_v2(IntPtr db);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int n,out IntPtr statement,IntPtr tail);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr statement);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr statement);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_double(IntPtr statement,int index,double value);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern long sqlite3_column_int64(IntPtr statement,int index);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_bytes(IntPtr statement,int index);
            [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_blob(IntPtr statement,int index);
            IntPtr handle;static byte[] UTF8(string s)=>Encoding.UTF8.GetBytes(s+"\0");string Error=>Marshal.PtrToStringAnsi(sqlite3_errmsg(handle));
            public ReadOnlyDatabase(string path){int code=sqlite3_open_v2(UTF8(new Uri(path).AbsoluteUri+"?mode=ro&immutable=1"),out handle,1|64,IntPtr.Zero);if(code!=0){string error=Error;Dispose();throw new IOException(error);}}
            public QueryRows Query(string sql){if(sqlite3_prepare_v2(handle,UTF8(sql),-1,out var statement,IntPtr.Zero)!=0)throw new IOException(Error);return new QueryRows(this,statement);}
            public void Dispose(){if(handle!=IntPtr.Zero){sqlite3_close_v2(handle);handle=IntPtr.Zero;}}
            public sealed class QueryRows:IDisposable
            {
                readonly ReadOnlyDatabase owner;IntPtr handle;public QueryRows(ReadOnlyDatabase db,IntPtr value){owner=db;handle=value;}
                public void Bind(int index,double value){if(sqlite3_bind_double(handle,index,value)!=0)throw new IOException(owner.Error);}
                public bool Step(){int code=sqlite3_step(handle);if(code==100)return true;if(code==101)return false;throw new IOException(owner.Error);}
                public long Integer(int index)=>sqlite3_column_int64(handle,index);
                public byte[] Bytes(int index){int length=sqlite3_column_bytes(handle,index);if(length<0||length>1000000)throw new InvalidDataException("Oversized source field");var data=new byte[length];if(length>0)Marshal.Copy(sqlite3_column_blob(handle,index),data,0,length);return data;}
                public string Text(int index)=>Encoding.UTF8.GetString(Bytes(index));
                public void Dispose(){if(handle!=IntPtr.Zero){sqlite3_finalize(handle);handle=IntPtr.Zero;}}
            }
        }
    }
}
