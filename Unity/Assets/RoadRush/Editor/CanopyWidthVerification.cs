using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using RoadRush.Core;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class CanopyWidthVerification
    {
        static GeoRegion Fixture()
        {
            var r=new GeoRegion{id="canopy-width-fixture",name="Isolated canopy width fixture",countryCode="GB",drivingSide=-1,latitude=51.67,longitude=-3.14,minX=-250,minZ=-250,width=500,depth=500,nx=2,nz=2,heights=new[]{20f,20f,20f,20f},preparedWidths=true,waters=Array.Empty<GeoWater>(),buildings=Array.Empty<GeoBuilding>(),areas=Array.Empty<GeoArea>(),route=new[]{new Vector3(-40,10,0),new Vector3(40,10,0)},roads=new[]{new GeoRoad{id="wide",start="a",end="j",kind="residential",width=12,limit=30,points=new[]{new Vector3(-40,10,0),new Vector3(0,10,0)}},new GeoRoad{id="narrow",start="j",end="b",kind="residential",width=8.5f,limit=30,points=new[]{new Vector3(0,10,0),new Vector3(40,10,0)}}}};r.Prepare();return r;
        }
        public static void Run()
        {
            int checks=0;string previous=PlayerProfile.StorageOverride;
            void Check(bool pass,string why){if(!pass)throw new Exception("CANOPY WIDTH FAIL "+why);checks++;Debug.Log("CANOPY WIDTH PASS "+why);}
            try
            {
                PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/canopy-width-profile.json");var r=Fixture();Check(r.Land(0,0)==0&&!r.IsWater(0,0),"optional absent land-cover grid behaves as unspecified dry land");var tree=new GeoTree{id="probe",x=-2,z=8.3f,crownRadius=1.6f,height=8,seed=DetailCanopyPacks.Seed("probe")};Check(!new DetailCanopyPacks.Clearance(r).Allows(tree),"legacy broad road excludes the near-edge crown");
                var profiles=new[]{new RoadWidthProfileDto{roadId="wide",distances=new[]{0f,40f},widths=new[]{12f,8.5f},derivatives=new float[2]},new RoadWidthProfileDto{roadId="narrow",distances=new[]{0f,40f},widths=new[]{8.5f,8.5f},derivatives=new float[2]}};
                Check(LegacyRoadWidthPreparation.TryInstall(r,1,profiles,out _),"inward canonical field installs");var plan=RoadWidthWorldPreparation.PrepareSurfacesOwned(r,CancellationToken.None);var clearance=new DetailCanopyPacks.Clearance(r,plan);
                Check(clearance.Allows(tree),"exact final pavement permits the unchanged crown clear of the narrowed approach");Check(!clearance.Allows(new GeoTree{id="inside",x=-2,z=0,crownRadius=1.6f,height=8}),"exact final pavement forbids a crown on asphalt");
                bool rejected=false;try{new DetailCanopyPacks.Clearance(r);}catch(InvalidDataException){rejected=true;}Check(rejected,"explicit widths cannot finalize canopy against a missing surface plan");
                r.trees=new[]{tree};r.canopyCoverage=new[]{new GeoCanopyCoverage{id="coverage",sourceHash=new string('a',64),minX=-200,maxX=200,minZ=-200,maxZ=200,boundary=new[]{new Vector3(-200,0,-200),new Vector3(200,0,-200),new Vector3(200,0,200),new Vector3(-200,0,200),new Vector3(-200,0,-200)},candidateCount=1,acceptedCount=1}};
                string canonical=WorldSnapshotCodec.CanonicalJson(r),epoch=WorldSnapshotCodec.Epoch(r),map=WorldSnapshotCodec.NetworkId(r);var peer=WorldSnapshotCodec.Decode(canonical,map,epoch);
                Check(peer.trees.Length==1&&peer.canopyCoverage[0].acceptedCount==1&&WorldSnapshotCodec.CanonicalJson(peer)==canonical,"nonzero-tree explicit-width peer snapshot round-trips without filtering pinned trees or counts");
                var bad=JObject.Parse(canonical);bad["trees"][0]["z"]=0;string altered=bad.ToString(Newtonsoft.Json.Formatting.None);var changed=JsonUtility.FromJson<GeoRegion>(altered);string badEpoch=WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,map,JsonUtility.ToJson(changed));rejected=false;
                try{WorldSnapshotCodec.Decode(altered,map,badEpoch);}catch(InvalidDataException e){rejected=e.Message.Contains("canopy road/building/water/service/spawn clearance");}Check(rejected,"matching-digest tree moved onto final asphalt is rejected by exact clearance");Check(WorldSnapshotCodec.CanonicalJson(peer)==canonical,"rejected tree payload leaves independent accepted snapshot unchanged");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/canopy-width.txt","PASS "+checks+" actual Unity field/pavement/nonzero-tree peer checks. No pack activation or native placement claim.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=previous;}
        }
    }
}
