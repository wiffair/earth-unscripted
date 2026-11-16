using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;

namespace EarthUnscripted.Packs
{
    /// <summary>Discoverable event metadata. Unsupported events never supply fallback racing behaviour.</summary>
    public sealed class PackCircuitEvent
    {
        public readonly string Id, Name, LayoutId, ManifestSha256, LaunchToken, UnavailableReason;
        public readonly bool Readable;
        internal readonly int Schema;
        internal readonly PackFile Payload, RulesPayload;
        internal PackCircuitEvent(string id,string name,string layout,string hash,PackFile payload,PackFile rules,bool readable,string reason,int schema)
        {
            Id=id;Name=name;LayoutId=layout;ManifestSha256=hash;Payload=payload;RulesPayload=rules;Readable=readable;Schema=schema;
            UnavailableReason=readable?null:reason;
            LaunchToken="packcircuit-"+WorldPackPolicy.Hash(Encoding.UTF8.GetBytes("EarthUnscriptedCircuitEventLaunch1|"+hash+"|"+id+"|"+payload.Sha256+"|"+rules.Sha256));
        }
    }
    /// <summary>Owned immutable event values. Economy remains game authority, never pack-supplied code or credits.</summary>
    public sealed class PackCircuitEventSettings
    {
        public readonly PackCircuitEvent Event;
        public readonly RacingRuleSet Rules;
        public readonly int Mode, Laps, Difficulty;
        public readonly string PitComplexId;
        internal PackCircuitEventSettings(PackCircuitEvent item,RacingRuleSet rules,int mode,int laps,int difficulty,string pitComplexId=null)
        {Event=item;Rules=rules;Mode=mode;Laps=laps;Difficulty=difficulty;PitComplexId=pitComplexId;}
    }
    public sealed partial class WorldPackCircuitReader
    {
        public IReadOnlyList<PackCircuitEvent> Events { get; private set; }
        void ReadEventCatalogue(JObject value,WorldPackManifest manifest,PackCompatibility compatible,string catalogueId)
        {
            var rows=new List<PackCircuitEvent>();Events=new ReadOnlyCollection<PackCircuitEvent>(rows);
            if(value.Property("events")==null)return;
            var events=value["events"] as JArray;
            WorldPackPolicy.Require(events!=null&&events.Count<=128,"circuit event catalogue budget");
            var ids=new HashSet<string>(StringComparer.Ordinal);var elements=new HashSet<string>(StringComparer.Ordinal);var paths=new HashSet<string>(StringComparer.Ordinal);
            foreach(var token in events)
            {
                WorldPackPolicy.Require(token is JObject,"circuit event descriptor");var row=(JObject)token;
                string id=Id(row,"id"),layoutId=Id(row,"layoutId"),elementId=Id(row,"elementId"),rulesId=Id(row,"rulesElementId");
                var layout=Layout(layoutId);string file=Text(row,"payload",180),ruleFile=Text(row,"rulesPayload",180);
                WorldPackPolicy.Require(ids.Add(id)&&elements.Add(elementId)&&paths.Add(file)&&WorldPackPolicy.SafePath(file)&&WorldPackPolicy.SafePath(ruleFile),"duplicate/unsafe circuit event binding");
                var eventDecision=compatible.Decisions.SingleOrDefault(d=>d.Element.Id==elementId);
                var ruleDecision=compatible.Decisions.SingleOrDefault(d=>d.Element.Id==rulesId);
                WorldPackPolicy.Require(eventDecision!=null&&ruleDecision!=null&&eventDecision.Element.Dependencies.Contains(catalogueId)
                    &&eventDecision.Element.Dependencies.Contains(layout.ElementId)&&eventDecision.Element.Dependencies.Contains(rulesId)
                    &&ruleDecision.Element.Dependencies.Contains(catalogueId),"event/layout/rules dependencies");
                WorldPackPolicy.Require(eventDecision.Element.Variants.Any(v=>v.Files.Contains(file))&&ruleDecision.Element.Variants.Any(v=>v.Files.Contains(ruleFile)),"event/rules payload binding");
                var payload=manifest.Files.Single(f=>f.Path==file);var rules=manifest.Files.Single(f=>f.Path==ruleFile);
                bool readable=layout.Readable&&eventDecision.Loaded&&ruleDecision.Loaded
                    &&eventDecision.Variant.Feature=="circuit-event"&&(eventDecision.Variant.Schema==1||eventDecision.Variant.Schema==2)
                    &&ruleDecision.Variant.Feature=="racing-rules"&&ruleDecision.Variant.Schema==1;
                if(readable)WorldPackPolicy.Require(eventDecision.Element.AffectsGameplay&&ruleDecision.Element.AffectsGameplay
                    &&eventDecision.Variant.Files.Count==1&&eventDecision.Variant.Files[0]==file
                    &&ruleDecision.Variant.Files.Count==1&&ruleDecision.Variant.Files[0]==ruleFile
                    &&payload.Bytes>0&&payload.Bytes<=16384&&rules.Bytes>0&&rules.Bytes<=RacingRuleSetJson.MaximumBytes,"event/rules gameplay contract");
                int schema=eventDecision.Loaded?eventDecision.Variant.Schema:0;
                if(readable&&schema==2)
                {
                    var service=compatible.Decisions.SingleOrDefault(d=>d.Loaded&&d.Variant.Feature=="circuit-pit-complexes"&&d.Variant.Schema==1&&d.Element.Dependencies.Contains(layout.ElementId));
                    WorldPackPolicy.Require(service!=null&&eventDecision.Element.Dependencies.Contains(service.Element.Id),"event pit complex dependency");
                }
                rows.Add(new PackCircuitEvent(id,Text(row,"name",160),layoutId,manifest.ManifestSha256,payload,rules,readable,"Event, rules or layout representation unsupported",schema));
            }
        }
        public PackCircuitEvent Event(string id)
        {var item=Events.SingleOrDefault(e=>e.Id==id);WorldPackPolicy.Require(item!=null,"selected circuit event absent");return item;}
        public PackCircuitEventSettings ReadEvent(string id,CancellationToken cancellation=default)
        {
            var item=Event(id);WorldPackPolicy.Require(item.Readable,"selected circuit event unsupported");
            var bytes=Encoding.UTF8.GetBytes(ReadChecked(item.Payload,16384,cancellation));
            var value=RacingRuleSetJson.ReadStrictObject(bytes,16384);
            string[] fields=item.Schema==2?new[]{"format","schema","id","layoutId","mode","laps","difficulty","pitComplexId"}:new[]{"format","schema","id","layoutId","mode","laps","difficulty"};
            WorldPackPolicy.Require(value.Count==fields.Length&&fields.All(f=>value.Property(f)!=null)
                &&value["format"]?.Type==JTokenType.String&&(string)value["format"]=="EarthUnscriptedCircuitEvent"
                &&value["schema"]?.Type==JTokenType.Integer&&(long)value["schema"]==item.Schema
                &&value["id"]?.Type==JTokenType.String&&(string)value["id"]==item.Id
                &&value["layoutId"]?.Type==JTokenType.String&&(string)value["layoutId"]==item.LayoutId,"circuit event identity/shape");
            int Integer(string field,int min,int max){var t=value[field];WorldPackPolicy.Require(t?.Type==JTokenType.Integer,"event integer "+field);long n=(long)t;WorldPackPolicy.Require(n>=min&&n<=max,"event range "+field);return(int)n;}
            var rules=RacingRuleSetJson.Parse(Encoding.UTF8.GetBytes(ReadChecked(item.RulesPayload,RacingRuleSetJson.MaximumBytes,cancellation)));
            cancellation.ThrowIfCancellationRequested();
            string pitId=item.Schema==2?Id(value,"pitComplexId"):null;
            return new PackCircuitEventSettings(item,rules,Integer("mode",0,3),Integer("laps",1,5),Integer("difficulty",0,2),pitId);
        }
    }
}
