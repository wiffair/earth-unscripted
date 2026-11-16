using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EarthUnscripted
{
    // Read-only UI inventory. Captured roots must come from the existing approved
    // country-root discovery on the main thread; this never opens a country DB.
    public enum DetailInventoryState { Verified, SelectedRevision, Superseded, Duplicate, Conflict, Invalid, RootDiscoveryLimit, Deferred, Unreadable }
    public enum DetailInventoryCoverage { Unknown, InsideRequestedArea, OutsideRequestedArea }
    public sealed class DetailInventoryRoot
    {
        public readonly string Country,DetailFolder;
        public readonly bool CountryMetadataReady,DatabasePresent;
        public string CountryFolder=>Path.GetDirectoryName(DetailFolder);
        public DetailInventoryRoot(string country,string folder,bool metadataReady=true,bool databasePresent=true){Country=country??"";DetailFolder=folder??"";CountryMetadataReady=metadataReady;DatabasePresent=databasePresent;}
    }
    public sealed class DetailInventoryRow
    {
        public readonly string Country,Folder,Kind,Id,RevisionSeries,Identity,Version,Licence,Attribution,Reason,ValidationScope;
        public readonly long? Revision,ManifestBytes,PayloadBytes;
        public readonly DetailInventoryState State;
        public readonly DetailInventoryCoverage Coverage;
        public readonly bool DetailRequested,SelectedForNextPreparation,CountryDataAvailable;
        public readonly IReadOnlyList<string> SourceURLs;
        public long? KnownRuntimeBytes=>ManifestBytes.HasValue&&PayloadBytes.HasValue?ManifestBytes+PayloadBytes:null;
        // These facts cannot be inferred from source URLs or a current installed folder.
        public long? ArchiveBytes=>null;
        public string RemoteDownloadURL=>null;
        public string ActivePreparedRevision=>null;
        internal DetailInventoryRow(DetailPackInventory.Draft d)
        {
            Country=d.Country;Folder=d.Folder;Kind=d.Kind;Id=d.Id;RevisionSeries=d.Series;Identity=d.Identity;Version=d.Version;Licence=d.Licence;Attribution=d.Attribution;Reason=d.Reason;ValidationScope=d.ValidationScope;
            Revision=d.Revision;ManifestBytes=d.ManifestBytes;PayloadBytes=d.PayloadBytes;State=d.State;Coverage=d.Coverage;DetailRequested=d.Requested;CountryDataAvailable=d.CountryAvailable;
            SelectedForNextPreparation=d.Next;
            SourceURLs=new ReadOnlyCollection<string>(d.SourceURLs.ToArray());
        }
    }
    public sealed class DetailInventorySnapshot
    {
        public readonly IReadOnlyList<DetailInventoryRow> Rows;
        public readonly IReadOnlyList<string> Issues;
        public readonly IReadOnlyList<DetailInventoryRoot> CountryRoots;
        public readonly bool SelectionComplete,Cancelled;
        public readonly long ValidatedPayloadBytes;
        public readonly string CheckedAtUtc;
        internal DetailInventorySnapshot(IEnumerable<DetailPackInventory.Draft> rows,IEnumerable<string> issues,bool complete,bool cancelled,long bytes,IEnumerable<DetailInventoryRoot> countryRoots=null)
        {
            Rows=new ReadOnlyCollection<DetailInventoryRow>(rows.OrderBy(r=>r.Country,StringComparer.Ordinal).ThenBy(r=>r.Kind,StringComparer.Ordinal).ThenBy(r=>r.Id,StringComparer.Ordinal).ThenBy(r=>r.Folder,StringComparer.Ordinal).Select(r=>new DetailInventoryRow(r)).ToArray());
            CountryRoots=new ReadOnlyCollection<DetailInventoryRoot>((countryRoots??Array.Empty<DetailInventoryRoot>()).ToArray());
            Issues=new ReadOnlyCollection<string>(issues.ToArray());SelectionComplete=complete;Cancelled=cancelled;ValidatedPayloadBytes=bytes;CheckedAtUtc=DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }
    }
    public sealed class DetailInventoryJob : IDisposable
    {
        readonly CancellationTokenSource cancel=new CancellationTokenSource();bool disposed;
        public readonly Task<DetailInventorySnapshot> Completion;
        public DetailInventoryJob(IEnumerable<DetailInventoryRoot> roots,WorldPlace place,bool requested)
        {
            // Defensive copies precede Task.Run; no mutable profile/place/Unity objects
            // or lazy filesystem enumerators cross into the worker.
            var copied=(roots??Array.Empty<DetailInventoryRoot>()).Take(DetailPackInventory.MaximumRoots+1).Select(r=>new DetailInventoryRoot(r?.Country,r?.DetailFolder,r?.CountryMetadataReady??false,r?.DatabasePresent??false)).ToArray();
            var area=DetailPackInventory.CopyPlace(place);var token=cancel.Token;
            Completion=Task.Run(()=>DetailPackInventory.Scan(copied,area,requested,token));
        }
        public DetailInventoryJob(DetailInventoryDiscoveryRequest discovery,WorldPlace place,bool requested)
        {
            if(discovery==null)throw new ArgumentNullException(nameof(discovery));
            var area=DetailPackInventory.CopyPlace(place);var token=cancel.Token;
            Completion=Task.Run(()=>{try{var found=DetailInventoryDiscovery.Discover(discovery,token);return DetailPackInventory.Scan(found.Roots,area,requested,token,found.Complete,found.Issues);}catch(OperationCanceledException){return DetailPackInventory.Scan(Array.Empty<DetailInventoryRoot>(),area,requested,token);}});
        }
        public void Cancel(){if(!disposed)cancel.Cancel();}
        public void Dispose(){if(disposed)return;disposed=true;cancel.Cancel();cancel.Dispose();}
    }
    public static class DetailPackInventory
    {
        public const int MaximumRoots=16,MaximumDirectoriesPerRoot=512,MaximumRows=256,MaximumManifestBytes=256000;
        public const long MaximumValidatedPayloadBytes=16L*1024*1024;
        const int MaximumPerKindPerRoot=64;
        internal sealed class Draft
        {
            public string Country="",Folder="",Kind="",Id="",Series="",Identity="",Version="",Licence="",Attribution="",Reason="",ValidationScope="";
            public long? Revision,ManifestBytes,PayloadBytes;public bool Requested,Next,CountryAvailable=true,DatabasePresent=true;
            public DetailInventoryState State;public DetailInventoryCoverage Coverage;
            public readonly List<string> SourceURLs=new List<string>();public DetailBuildingPack Building;public DetailCanopyPack Canopy;
            public bool Valid=>Building!=null||Canopy!=null;
        }
        internal static WorldPlace CopyPlace(WorldPlace p)=>p==null?null:new WorldPlace{name=p.name,country=p.country,country_code=p.country_code,timezone=p.timezone,latitude=p.latitude,longitude=p.longitude,size=p.size,drivingSide=p.drivingSide};
        static string Clean(string text,int length=180)=>new string((text??"").Take(length).Select(c=>char.IsControl(c)?' ':c).ToArray());
        static bool Present(string path)
        {
            try{return (File.GetAttributes(path)&FileAttributes.Directory)==0;}
            catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
        }
        static bool Linked(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static byte[] ReadHeader(string path,CancellationToken token)
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length<1||stream.Length>MaximumManifestBytes)throw new InvalidDataException("Manifest exceeds size limit");
                var bytes=new byte[(int)stream.Length];int offset=0;
                while(offset<bytes.Length){token.ThrowIfCancellationRequested();int n=stream.Read(bytes,offset,Math.Min(32768,bytes.Length-offset));if(n==0)throw new EndOfStreamException("Manifest changed while reading");offset+=n;}
                if(stream.ReadByte()!=-1)throw new InvalidDataException("Manifest changed while reading");return bytes;
            }
        }
        static JObject Metadata(byte[] bytes)
        {
            using(var text=new StreamReader(new MemoryStream(bytes),Encoding.UTF8,true))
            using(var reader=new JsonTextReader(text){MaxDepth=64})return JObject.Load(reader);
        }
        static Draft Inspect(string country,string folder,string kind,bool requested,ref long used,CancellationToken token,bool countryAvailable=true,bool databasePresent=true)
        {
            var d=new Draft{Country=country,Folder=folder,Kind=kind,Requested=requested,CountryAvailable=countryAvailable,DatabasePresent=databasePresent,State=DetailInventoryState.Invalid};
            string stem=kind=="Buildings"?"buildings":"canopy",manifest=Path.Combine(folder,stem+"-pack.json"),payload=Path.Combine(folder,stem+".json");
            try
            {
                token.ThrowIfCancellationRequested();
                if(Linked(folder)||Linked(manifest)||File.Exists(payload)&&Linked(payload)){d.State=DetailInventoryState.Deferred;d.Reason="Linked files/directories were not inspected";return d;}
                var bytes=ReadHeader(manifest,token);d.ManifestBytes=bytes.Length;var meta=Metadata(bytes);
                d.Id=Clean((string)meta["id"],100);d.Version=meta["version"]?.Type==JTokenType.String?Clean((string)meta["version"],160):"";
                d.PayloadBytes=new FileInfo(payload).Length;
                long maximum=kind=="Buildings"?4000000:4*1024*1024;
                if(d.PayloadBytes<1||d.PayloadBytes>maximum)throw new InvalidDataException("Payload exceeds format limit");
                if(used+d.PayloadBytes.Value>MaximumValidatedPayloadBytes){d.State=DetailInventoryState.Deferred;d.Reason="Inventory validation byte budget reached; refresh with a narrower root set";return d;}
                used+=d.PayloadBytes.Value;token.ThrowIfCancellationRequested();
                // Existing validators perform the authoritative payload hash and policy checks.
                // Each call is bounded to one<=4MiB payload; cancellation is checked around it.
                if(kind=="Buildings")
                {
                    d.Building=new DetailBuildingPack(folder);d.Id=d.Building.Id;d.Series=d.Building.RevisionSeries;d.Revision=d.Building.Revision;d.Identity=d.Building.Identity;d.Attribution=d.Building.Attribution;
                    d.ValidationScope="Manifest and payload verified; geometry validation occurs during area preparation";
                }
                else
                {
                    d.Canopy=new DetailCanopyPack(folder);d.Id=d.Canopy.Id;d.Series=d.Canopy.RevisionSeries;d.Revision=d.Canopy.Revision;d.Identity=d.Canopy.Identity;d.Attribution=d.Canopy.Attribution;
                    d.ValidationScope="Manifest and candidates verified; final clearances occur during area preparation";
                }
                token.ThrowIfCancellationRequested();
                if(Hash(bytes)!=d.Identity)throw new InvalidDataException("Manifest changed during inspection");
                d.Licence=(string)meta["licence"];d.State=DetailInventoryState.Verified;if(!databasePresent)d.Reason="Detail files verified; country map database is missing";
                if(meta["sourceURLs"] is JArray urls)foreach(var url in urls.Take(8)){if(url.Type!=JTokenType.String)continue;string text=(string)url;if(text!=null&&text.Length<=2048&&Uri.TryCreate(text,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps)d.SourceURLs.Add(text);}
            }
            catch(OperationCanceledException){throw;}
            catch(Exception e){d.Building=null;d.Canopy=null;d.State=e is IOException||e is UnauthorizedAccessException?DetailInventoryState.Unreadable:DetailInventoryState.Invalid;d.Reason=Clean(e.Message);}
            return d;
        }
        public static DetailInventorySnapshot Scan(IEnumerable<DetailInventoryRoot> roots,WorldPlace place,bool requested,CancellationToken token,bool discoveryComplete=true,IEnumerable<string> discoveryIssues=null)
        {
            var rows=new List<Draft>();var issues=(discoveryIssues??Array.Empty<string>()).Take(32).Select(x=>Clean(x,350)).ToList();long used=0;bool complete=discoveryComplete,cancelled=false;DetailInventoryRoot[] inventoryRoots=Array.Empty<DetailInventoryRoot>();
            try
            {
                var captured=(roots??Array.Empty<DetailInventoryRoot>()).Take(MaximumRoots+1).ToArray();
                if(captured.Length>MaximumRoots)return new DetailInventorySnapshot(rows,new[]{"Inventory root limit exceeded; no partial selection reported"},false,false,used);
                if(captured.Any(r=>r==null||string.IsNullOrWhiteSpace(r.DetailFolder)))throw new InvalidDataException("Inventory root folder is missing");
                var normalized=captured.Select(r=>new DetailInventoryRoot(r.Country,Path.GetFullPath(r.DetailFolder),r.CountryMetadataReady,r.DatabasePresent)).GroupBy(r=>r.Country+"|"+r.DetailFolder,StringComparer.Ordinal).Select(g=>g.First()).OrderBy(r=>r.Country,StringComparer.Ordinal).ThenBy(r=>r.DetailFolder,StringComparer.Ordinal).ToArray();inventoryRoots=normalized;
                foreach(var root in normalized)
                {
                    token.ThrowIfCancellationRequested();if(!Directory.Exists(root.DetailFolder))continue;
                    string[] folders;
                    try
                    {
                        folders=Directory.EnumerateDirectories(root.DetailFolder).Take(MaximumDirectoriesPerRoot+1).ToArray();
                        if(folders.Length>MaximumDirectoriesPerRoot){complete=false;issues.Add(Clean(root.Country)+": directory scan limit reached; no partial root inspection");continue;}
                        Array.Sort(folders,StringComparer.Ordinal);
                    }
                    catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){complete=false;issues.Add(Clean(root.Country)+": "+Clean(e.Message));continue;}
                    foreach(string kind in new[]{"Buildings","Canopy"})
                    {
                        string manifest=kind=="Buildings"?"buildings-pack.json":"canopy-pack.json";
                        var candidates=new List<string>();foreach(string folder in folders){token.ThrowIfCancellationRequested();if(Present(Path.Combine(folder,manifest)))candidates.Add(folder);}
                        if(candidates.Count>MaximumPerKindPerRoot)
                        {
                            issues.Add(Clean(root.Country)+": "+kind+" discovery skipped by existing64manifest policy");
                            foreach(string folder in candidates){if(rows.Count>=MaximumRows)throw new InvalidDataException("Inventory row limit reached; no partial selection reported");rows.Add(new Draft{Country=root.Country,Folder=folder,Kind=kind,Requested=requested,State=DetailInventoryState.RootDiscoveryLimit,Reason="Existing discovery refuses a partial catalogue above64manifests"});}
                            continue;
                        }
                        foreach(string folder in candidates)
                        {
                            if(rows.Count>=MaximumRows)throw new InvalidDataException("Inventory row limit reached; no partial selection reported");
                            var d=Inspect(root.Country,folder,kind,requested,ref used,token,root.CountryMetadataReady&&root.DatabasePresent,root.DatabasePresent);rows.Add(d);if(d.State==DetailInventoryState.Deferred)complete=false;
                        }
                    }
                }
                if(complete)
                {
                    var area=CopyPlace(place);
                    if(area!=null&&(double.IsNaN(area.latitude)||double.IsInfinity(area.latitude)||double.IsNaN(area.longitude)||double.IsInfinity(area.longitude)||area.latitude< -80||area.latitude>80||area.longitude< -180||area.longitude>180)){issues.Add("Requested area invalid; coverage remains unknown");area=null;}
                    Select(rows,area);token.ThrowIfCancellationRequested();
                }
                else foreach(var d in rows.Where(r=>r.Valid)){d.State=DetailInventoryState.Deferred;d.Reason="Payload verified; revision selection deferred because inventory is incomplete";}
            }
            catch(OperationCanceledException){cancelled=true;complete=false;issues.Add("Inventory cancelled; no partial selection reported");foreach(var d in rows.Where(r=>r.Valid)){d.State=DetailInventoryState.Deferred;d.Reason="Selection deferred after cancellation";}}
            catch(Exception e){complete=false;issues.Add(Clean(e.Message));foreach(var d in rows.Where(r=>r.Valid)){d.State=DetailInventoryState.Deferred;d.Reason="Selection deferred after inventory failure";}}
            return new DetailInventorySnapshot(rows,issues,complete,cancelled,used,inventoryRoots);
        }
        static void Select(List<Draft> rows,WorldPlace place)
        {
            foreach(var country in rows.Where(r=>r.Valid&&r.DatabasePresent).GroupBy(r=>r.Country,StringComparer.Ordinal))
            foreach(string kind in new[]{"Buildings","Canopy"})
            {
                var candidates=country.Where(r=>r.Kind==kind).ToArray();
                var selected=kind=="Buildings"?DetailBuildingPacks.SelectRevisions(candidates.Select(r=>r.Building)).Select(p=>p.Identity).ToArray():DetailCanopyPacks.SelectRevisions(candidates.Select(r=>r.Canopy)).Select(p=>p.Identity).ToArray();
                var selectedIds=new HashSet<string>(selected,StringComparer.Ordinal);
                foreach(var group in candidates.GroupBy(r=>r.Id,StringComparer.Ordinal))
                {
                    var identities=group.Select(r=>r.Identity).Distinct(StringComparer.Ordinal).ToArray();bool conflict=!group.Any(r=>selectedIds.Contains(r.Identity));
                    string reason=identities.Length>1&&group.Any(r=>!r.Revision.HasValue)?"Legacy and other identities have no safe explicit ordering":group.Select(r=>r.Series).Distinct(StringComparer.Ordinal).Count()>1?"Conflicting revision series":"Different content declares the same revision";
                    foreach(var identity in group.GroupBy(r=>r.Identity,StringComparer.Ordinal))
                    {
                        var ordered=identity.OrderByDescending(r=>r.CountryAvailable).ThenBy(r=>r.Folder,StringComparer.Ordinal).ToArray();
                        for(int i=0;i<ordered.Length;i++)
                        {
                            var d=ordered[i];
                            if(conflict){d.State=DetailInventoryState.Conflict;d.Reason=reason;}
                            else if(i>0){d.State=DetailInventoryState.Duplicate;d.Reason="Exact manifest identity already represented by another installed copy";}
                            else if(selectedIds.Contains(d.Identity)){d.State=DetailInventoryState.SelectedRevision;d.Reason="Revision chosen by existing game selection policy";}
                            else{d.State=DetailInventoryState.Superseded;d.Reason="A newer explicit revision is selected";}
                            if(place!=null)d.Coverage=string.Equals(place.country_code,d.Country,StringComparison.Ordinal)&&(d.Building!=null?d.Building.Intersects(place):d.Canopy.Intersects(place))?DetailInventoryCoverage.InsideRequestedArea:DetailInventoryCoverage.OutsideRequestedArea;
                        }
                    }
                }
                // Match Find's final identity ordering and eight-intersecting-tile cap.
                var inArea=candidates.Where(d=>d.State==DetailInventoryState.SelectedRevision&&d.Coverage==DetailInventoryCoverage.InsideRequestedArea).OrderBy(d=>d.Identity,StringComparer.Ordinal).ToArray();
                for(int i=0;i<inArea.Length;i++){inArea[i].Next=inArea[i].Requested&&inArea[i].CountryAvailable&&i<8;if(i>=8)inArea[i].Reason="Revision selected; outside existing eight-tile area preparation cap";}
            }
        }
    }
}
