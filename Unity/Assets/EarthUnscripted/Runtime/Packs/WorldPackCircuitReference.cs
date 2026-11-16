using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EarthUnscripted.Packs
{
    /// <summary>
    /// Immutable per-layout launch reference. Careers store its safe token; changing a
    /// different layout or installing an update cannot redirect older saved careers.
    /// Regional building preferences remain in their separate selection.json file.
    /// A reference is a lookup pin, never a certificate or authority to run pack code.
    /// </summary>
    public sealed class WorldPackCircuitReference
    {
        const int MaximumBytes = 8192;
        public readonly WorldPackSelection Pack;
        public readonly string LayoutId, Token, EventId;
        WorldPackCircuitReference(WorldPackSelection pack, string layout, string token, string eventId=null)
        {
            WorldPackPolicy.Require(pack != null && pack.Enabled && WorldPackPolicy.SafeId(layout) && IsToken(token), "circuit launch reference fields");
            WorldPackPolicy.Require(eventId==null||WorldPackPolicy.SafeId(eventId), "circuit event reference identity");
            Pack = pack; LayoutId = layout; Token = token; EventId=eventId;
        }
        public static bool IsToken(string token) => token != null && Regex.IsMatch(token, "\\Apackcircuit-[0-9a-f]{64}\\z");
        public static WorldPackCircuitReference For(WorldPackManifest pack, PackCircuitLayout layout)
        {
            WorldPackPolicy.Require(pack != null && pack.Kind == "circuit" && layout != null && layout.Readable
                && layout.ManifestSha256 == pack.ManifestSha256, "circuit reference requires its readable committed layout");
            return new WorldPackCircuitReference(WorldPackSelection.For(pack, false), layout.Id, layout.LaunchToken);
        }
        public static WorldPackCircuitReference ForEvent(WorldPackManifest pack,PackCircuitLayout layout,PackCircuitEvent item)
        {
            WorldPackPolicy.Require(pack!=null&&pack.Kind=="circuit"&&layout!=null&&layout.Readable&&item!=null&&item.Readable
                &&item.LayoutId==layout.Id&&item.ManifestSha256==pack.ManifestSha256&&layout.ManifestSha256==pack.ManifestSha256,"event reference committed bindings");
            return new WorldPackCircuitReference(WorldPackSelection.For(pack,false),layout.Id,item.LaunchToken,item.Id);
        }
        static string PathFor(string root, string token)
        {
            WorldPackPolicy.Require(IsToken(token), "circuit launch token");
            string path = Path.Combine(Path.GetFullPath(root), "circuit-launches", token + ".json");
            WorldPackStore.CheckAncestors(path); return path;
        }
        public static WorldPackCircuitReference Load(string root, string token, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested(); string path = PathFor(root, token); byte[] bytes;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                WorldPackPolicy.Require(input.Length > 0 && input.Length <= MaximumBytes, "circuit launch reference budget");
                bytes = new byte[(int)input.Length]; int at = 0;
                while (at < bytes.Length) { cancellation.ThrowIfCancellationRequested(); int n = input.Read(bytes, at, bytes.Length - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                WorldPackPolicy.Require(input.ReadByte() == -1, "circuit launch reference changed during read");
            }
            using (var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
                { MaxDepth = 4, DateParseHandling = DateParseHandling.None })
            {
                var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                WorldPackPolicy.Require(value["format"]?.Type==JTokenType.Integer,"circuit reference format type");
                long format=(long)value["format"];WorldPackPolicy.Require(format==1||format==2,"circuit reference format");
                var fields = format==1?new[] { "format", "id", "revision", "manifestSha256", "layoutId", "token" }:new[]{"format","id","revision","manifestSha256","layoutId","token","eventId"};
                WorldPackPolicy.Require(!reader.Read() && value.Count == fields.Length && fields.All(f => value.Property(f) != null), "circuit launch reference shape");
                WorldPackPolicy.Require(value["format"].Type == JTokenType.Integer && ((long)value["format"] == 1 || (long)value["format"] == 2)
                    && value["revision"].Type == JTokenType.Integer && fields.Where(f => f != "format" && f != "revision").All(f => value[f].Type == JTokenType.String), "circuit launch reference value types");
                WorldPackPolicy.Require((string)value["token"] == token, "circuit launch token/file identity differs");
                return new WorldPackCircuitReference(WorldPackSelection.Pinned((string)value["id"], (long)value["revision"], (string)value["manifestSha256"]),
                    (string)value["layoutId"], token,format==2?(string)value["eventId"]:null);
            }
        }
        bool Same(WorldPackCircuitReference other) => other != null && Token == other.Token && LayoutId == other.LayoutId && EventId==other.EventId
            && Pack.Id == other.Pack.Id && Pack.Revision == other.Pack.Revision && Pack.ManifestSha256 == other.Pack.ManifestSha256;
        public void Store(string root, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested(); string path = PathFor(root, Token);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); WorldPackStore.CheckAncestors(path);
            if (File.Exists(path)) { WorldPackPolicy.Require(Same(Load(root, Token, cancellation)), "immutable circuit launch reference conflict"); return; }
            var value = new JObject { ["format"] = EventId==null?1:2, ["id"] = Pack.Id, ["revision"] = Pack.Revision,
                ["manifestSha256"] = Pack.ManifestSha256, ["layoutId"] = LayoutId, ["token"] = Token };
            if(EventId!=null)value["eventId"]=EventId;
            byte[] bytes = Encoding.UTF8.GetBytes(value.ToString(Formatting.None));
            string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                cancellation.ThrowIfCancellationRequested(); WorldPackStore.CheckAncestors(path);
                try { File.Move(stage, path); }
                catch (IOException) { if (!File.Exists(path)) throw; WorldPackPolicy.Require(Same(Load(root, Token, cancellation)), "concurrent circuit reference conflict"); }
            }
            finally { if (File.Exists(stage)) File.Delete(stage); }
        }
        public WorldPackCircuitReader Resolve(WorldPackStore store, PackEngineSupport engine, CancellationToken cancellation = default)
        {
            var installed = store.Locate(Pack); var reader = WorldPackCircuitReader.OpenVerified(store, installed, engine, cancellation);
            var layout = reader.Layout(LayoutId);
            WorldPackPolicy.Require(layout.Readable,"circuit launch layout unsupported");
            if(EventId==null)WorldPackPolicy.Require(layout.LaunchToken==Token,"circuit launch reference does not match selected payload");
            else {var item=reader.Event(EventId);WorldPackPolicy.Require(item.Readable&&item.LayoutId==LayoutId&&item.LaunchToken==Token,"circuit event reference does not match selected payloads");}
            return reader;
        }
    }
}
