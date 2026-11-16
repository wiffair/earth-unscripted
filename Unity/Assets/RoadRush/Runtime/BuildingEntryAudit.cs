using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace RoadRush
{
    // Opt-in diagnostics only; never changes acceptance, geometry or saved maps.
    public sealed class BuildingEntryAudit
    {
        public const int Capacity=16384;bool enabled;readonly List<Row> rows=new List<Row>();readonly Dictionary<string,Row> latest=new Dictionary<string,Row>();int dropped,duplicateTerminals;
        [Serializable] public sealed class Attempt{public string variant,reason;public int iterations,count;public float targetDrop;}
        [Serializable] public sealed class Row
        {
            public string sourceId,partId,candidateId,terminal="",frameFailure="",geometryReason="";public bool planned,groundKnown,bandKnown,accessKnown,nonFinite;
            public float floorY,pitch,highestGround,lowestGround,entryBaseY,ceilNumerator,thresholdY,nearGap,targetDrop;public int floors,selectedBand,stairCount,targetIterations;public string selectedVariant="";public Attempt[] attempts=Array.Empty<Attempt>();
            public void AddAttempt(BuildingAccessGeometry.SearchAttempt value){if(attempts.Length>=3)return;var list=new List<Attempt>(attempts);list.Add(new Attempt{variant=value.Variant==0?"Low":value.Variant==1?"Mid":"High",reason=value.Accepted?"Accepted":value.RoadBlocked?"RoadBankReject":value.Reason.ToString(),iterations=value.Iterations,count=value.Count,targetDrop=value.TargetDrop});attempts=list.ToArray();}
            internal void Clean(){floorY=Safe(floorY);pitch=Safe(pitch);highestGround=Safe(highestGround);lowestGround=Safe(lowestGround);entryBaseY=Safe(entryBaseY);ceilNumerator=Safe(ceilNumerator);thresholdY=Safe(thresholdY);nearGap=Safe(nearGap);targetDrop=Safe(targetDrop);foreach(var a in attempts)a.targetDrop=Safe(a.targetDrop);}
            float Safe(float v){if(!float.IsNaN(v)&&!float.IsInfinity(v))return v;nonFinite=true;return 0;}
        }
        [Serializable] public sealed class Count{public string category;public int count;}
        [Serializable] public sealed class Report{public bool enabled;public int candidates,planned,accepted,pending,dropped,duplicateTerminals;public Count[] outcomes;public Row[] rows;}
        public bool Enabled
        {
            get=>enabled;
            set
            {
                bool allowed=Array.IndexOf(Environment.GetCommandLineArgs(),"-roadRushEntryAudit")>=0;
#if UNITY_EDITOR
                allowed|=!string.IsNullOrEmpty(PlayerProfile.StorageOverride);
#endif
                enabled=value&&allowed;
            }
        }
        public BuildingEntryAudit(){Enabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-roadRushEntryAudit")>=0;}
        public Row Begin(string source,string part,string candidate,float floor,float pitch,int floors)
        {
            if(!enabled)return null;if(rows.Count>=Capacity){dropped++;latest.Remove(candidate);return null;}
            var row=new Row{sourceId=source,partId=part,candidateId=candidate,floorY=floor,pitch=pitch,floors=floors};rows.Add(row);latest[candidate]=row;return row;
        }
        public Row Find(string candidate)=>enabled&&latest.TryGetValue(candidate,out var row)?row:null;
        public void Finish(Row row,string terminal){if(row==null)return;if(row.terminal.Length>0){duplicateTerminals++;return;}row.terminal=terminal;}
        public string ExportJson()
        {
            foreach(var row in rows)row.Clean();var report=new Report{enabled=enabled,candidates=rows.Count,planned=rows.Count(r=>r.planned),accepted=rows.Count(r=>r.terminal=="AcceptedDirect"||r.terminal=="AcceptedFlight"),pending=rows.Count(r=>r.terminal.Length==0),dropped=dropped,duplicateTerminals=duplicateTerminals,rows=rows.ToArray(),outcomes=rows.GroupBy(r=>r.terminal.Length==0?"Pending":r.terminal).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new Count{category=g.Key,count=g.Count()}).ToArray()};return JsonUtility.ToJson(report,true);
        }
    }
}
