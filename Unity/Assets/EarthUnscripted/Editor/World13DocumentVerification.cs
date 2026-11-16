using System;using System.IO;using System.Linq;using System.Text;using System.Security.Cryptography;using UnityEditor;using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class World13DocumentVerification
    {
        static int checks;static string Compatibility=>"EarthUnscripted/"+Application.version+"/geometry13";
        static readonly string Evidence=new string('a',64);
        static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("WORLD13 FAIL "+why);checks++;Debug.Log("WORLD13 PASS "+why);}
        static void Reject(Action action,string why){try{action();}catch(Exception e)when(e is InvalidDataException||e is Newtonsoft.Json.JsonException||e is NotSupportedException){Check(true,why);return;}throw new InvalidOperationException("WORLD13 accepted "+why);}
        static void RejectOwnership(Action action,string why){try{action();}catch(InvalidOperationException){Check(true,why);return;}throw new InvalidOperationException("WORLD13 accepted "+why);}
        static byte[] Bytes(World13Document d)=>new UTF8Encoding(false,true).GetBytes(WorldFieldJson.Write(d));
        static World13ReadContext Raw(byte[] b)=>World13ReadContext.FromVerifiedRawReceipt(World13ReadContext.RawOrigin.ExplicitAuthoredSource,Compatibility,World13DocumentCodec.Hash(b),Evidence);
        static World13ReadContext Prepared(byte[] b)=>World13ReadContext.FromVerifiedBundleReceipt(Compatibility,"authored-v1",World13DocumentCodec.Hash(b),Evidence);
        static string HashFile(string p){if(!File.Exists(p))return "missing";using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
        static float NegZero()=>BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        static string SourceDigest(World13Document d)
        {var source=d.geography.roads.Select(r=>WorldFieldJson.Clone(r)).ToArray();foreach(var r in source)r.sourceNodeIds=d.roadAuthority.sourceRoads.Single(n=>n.roadId==r.id).nodeIds;return RoadSourceIdentity.Compute(source);}
        static World13Document Fixture()
        {
            var g=new GeoRegion{id="raw13-test",name="Owned precise source",countryCode="GB",timeZone="Europe/London",attribution="Original test",datasetVersion="exact-source-v1",datasetSource=null,networkMapId="",latitude=BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(51.6)+1),longitude=-3.1,drivingSide=-1,preparedWidths=false,datum=0,minX=-100,minZ=-100,width=200,depth=200,nx=2,nz=2,heights=new[]{25f,25f,25f,25f},landNx=0,landNz=0,landCover=null,waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=Array.Empty<GeoBuilding>(),route=new[]{new Vector3(0,25,0),new Vector3(0,25,40)}};
            g.roads=new[]{new GeoRoad{id="r1",start="10",end="20",name="Narrow original",kind="residential",width=1.25f,limit=30,points=new[]{new Vector3(NegZero(),25,0),new Vector3(0,25,40)}},new GeoRoad{id="r2",start="30",end="40",name="Second",kind="residential",width=2.75f,limit=30,points=new[]{new Vector3(50,25,0),new Vector3(50,25,40)}}};
            var rows=new[]{new SourceRoad13Metadata{roadId="r2",nodeIds=new[]{"30","40"}},new SourceRoad13Metadata{roadId="r1",nodeIds=new[]{"10","20"}}};
            var source=g.roads.Select(r=>WorldFieldJson.Clone(r)).ToArray();foreach(var r in source)r.sourceNodeIds=rows.Single(n=>n.roadId==r.id).nodeIds;
            return new World13Document{compatibility=Compatibility,geography=g,roadAuthority=new RoadAuthorityMetadata13{schema=0,layoutRecipe="authored-v1",sourceRoads=rows,sourceDigest=RoadSourceIdentity.Compute(source)}};
        }
        static void RawTests(string output)
        {
            var d=Fixture();string legacy=JsonUtility.ToJson(d.geography);var b=Bytes(d);World13DocumentCodec.Preflight(Encoding.UTF8.GetString(b));Check(true,"bounded typed envelope preflight");
            var verified=World13DocumentCodec.ReadVerifiedRaw(b,Raw(b));byte[] saved=World13DocumentCodec.WriteVerifiedRaw(verified);
            string path=Path.Combine(output,"raw-cache.json"),stage=path+".next";File.WriteAllBytes(stage,saved);File.Move(stage,path);
            var cached=World13DocumentCodec.ReadVerifiedRaw(File.ReadAllBytes(path),Raw(saved));var owned=cached.TakeOwnedDocument();
            Check(owned.geography.roads[0].width==1.25f&&owned.geography.roads[1].width==2.75f&&!owned.geography.preparedWidths,"verified raw widths never normalized");
            Check(owned.geography.roads[0].sourceNodeIds.SequenceEqual(new[]{"10","20"})&&owned.geography.roads[1].sourceNodeIds.SequenceEqual(new[]{"30","40"}),"source rows bind byroadID independent order");
            Check(!ReferenceEquals(owned.geography.roads[0].sourceNodeIds,owned.roadAuthority.sourceRoads.Single(n=>n.roadId=="r1").nodeIds),"NS source-node array is an owned clone");
            Check(BitConverter.SingleToInt32Bits(owned.geography.roads[0].points[0].x)==unchecked((int)0x80000000),"raw vector negative-zero float bits survive envelope/cache");
            Check(BitConverter.DoubleToInt64Bits(owned.geography.latitude)==BitConverter.DoubleToInt64Bits(d.geography.latitude),"raw double latitude bits survive envelope/cache");
            Check(owned.geography.datasetSource==null&&owned.geography.landCover==null&&owned.geography.cornerNames==null&&owned.geography.cornerDistances==null,"source null provenance/optional arrays preserved");
            Check(owned.geography.PreparedRoadWidths==null&&owned.geography.roadElevationSchema==0,"raw attach builds no field or legacy Prepare");
            Check(legacy==JsonUtility.ToJson(owned.geography),"NS metadata leaves actual legacy geography JSON byte-identical");
            d.geography.preparedWidths=owned.geography.preparedWidths=true;
            Check(WorldSnapshotCodec.CanonicalJson(d.geography)==WorldSnapshotCodec.CanonicalJson(owned.geography),"actual existing12 canonical unchanged by NS metadata");
            Check(WorldSnapshotCodec.Epoch(d.geography)==WorldSnapshotCodec.Epoch(owned.geography),"actual existing12 epoch unchanged by NS metadata");
            RejectOwnership(()=>cached.TakeOwnedDocument(),"exclusive document handoff cannot repeat");Reject(()=>World13DocumentCodec.WriteVerifiedRaw(cached),"whole-document writer refuses transferred ownership");
            Check(WorldFieldJson.Read<World13Document>(Encoding.UTF8.GetString(saved)).roadAuthority.sourceRoads.Length==2,"whole document persists nodes despite NS geography fields");
            Check(!WorldFieldJson.Write(owned.geography).Contains("sourceNodeIds"),"bare geography cannot persist13 metadata: envelope is mandatory");
        }
        static void PrecisionInspection()
        {
            var d=Fixture();d.roadAuthority.schema=1;double exact=BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1000d)+1),tiny=BitConverter.Int64BitsToDouble(1),negative=BitConverter.Int64BitsToDouble(long.MinValue);
            d.roadAuthority.elevationProfiles=new[]{new RoadElevationProfileDto{roadId="r1",distances=new[]{negative,tiny,exact},elevations=new[]{25f,25.123457f,26.234568f},gradeDerivatives=new[]{0f,.003456789f,.004567891f},crossfalls=new[]{0f,-.01234567f,.01234567f},crownRates=new[]{.025f,.025000003f,.03f}}};
            d.roadAuthority.junctionPatches=new[]{new RoadJunctionPatchDto{id="inspect",nodeId="10",kind="at-grade",boundary=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up,Vector2.zero},arms=new[]{new RoadJunctionArmDto{roadId="r1",sourceDistance=exact,direction=1,coreLength=1,collarLength=2,spans=new[]{new RoadJunctionSpanDto{roadId="r1",fromSourceDistance=tiny,toSourceDistance=exact}}}}}};
            d.roadAuthority.syntheticAnchors=new[]{new RoadSyntheticAnchorDto{id="inspection-only",kind="service",parentRoadId="r1",connectorRoadId="r2",sourcePin=Evidence,parentSourceDistance=exact,connectorSourceDistance=negative}};
            var b=Bytes(d);var candidate=World13DocumentCodec.ReadCandidate(b,Prepared(b));Check(!candidate.Admitted,"precision inspection is explicitly UNADMITTED");var copy=candidate.CopyForInspection();
            for(int i=0;i<3;i++)Check(BitConverter.DoubleToInt64Bits(copy.roadAuthority.elevationProfiles[0].distances[i])==BitConverter.DoubleToInt64Bits(d.roadAuthority.elevationProfiles[0].distances[i]),"profile double station bits "+i);
            Check(BitConverter.SingleToInt32Bits(copy.roadAuthority.elevationProfiles[0].elevations[1])==BitConverter.SingleToInt32Bits(d.roadAuthority.elevationProfiles[0].elevations[1])&&BitConverter.SingleToInt32Bits(copy.roadAuthority.elevationProfiles[0].gradeDerivatives[1])==BitConverter.SingleToInt32Bits(d.roadAuthority.elevationProfiles[0].gradeDerivatives[1])&&BitConverter.SingleToInt32Bits(copy.roadAuthority.elevationProfiles[0].crossfalls[1])==BitConverter.SingleToInt32Bits(d.roadAuthority.elevationProfiles[0].crossfalls[1])&&BitConverter.SingleToInt32Bits(copy.roadAuthority.elevationProfiles[0].crownRates[1])==BitConverter.SingleToInt32Bits(d.roadAuthority.elevationProfiles[0].crownRates[1]),"profile float control bits exact");
            var arm=copy.roadAuthority.junctionPatches[0].arms[0];Check(BitConverter.DoubleToInt64Bits(arm.sourceDistance)==BitConverter.DoubleToInt64Bits(exact),"17digit arm scalar exact");
            Check(BitConverter.DoubleToInt64Bits(arm.spans[0].fromSourceDistance)==1&&BitConverter.DoubleToInt64Bits(arm.spans[0].toSourceDistance)==BitConverter.DoubleToInt64Bits(exact),"double span scalars exact");
            Check(BitConverter.DoubleToInt64Bits(copy.roadAuthority.syntheticAnchors[0].parentSourceDistance)==BitConverter.DoubleToInt64Bits(exact)&&BitConverter.DoubleToInt64Bits(copy.roadAuthority.syntheticAnchors[0].connectorSourceDistance)==long.MinValue,"synthetic scalar and negative-zero exact");
            Reject(()=>World13DocumentCodec.ReadVerifiedRaw(b,Raw(b)),"prepared inspection metadata cannot be read as raw");
            Reject(()=>World13DocumentCodec.DecodePrepared(b,Prepared(b)),"malformed prepared candidate cannot attach");
            // A complete simple frozen-authority case STILL cannot be admitted:
            // new corner/cap/terrain proof is deliberately a separate requirement.
            var good=Fixture();foreach(var road in good.geography.roads)road.width=3f; // distinct authored wide numeric fixture, not decode normalization
            good.roadAuthority.sourceDigest=SourceDigest(good);good.roadAuthority.schema=1;good.roadAuthority.elevationProfiles=good.geography.roads.Select(r=>new RoadElevationProfileDto{roadId=r.id,distances=new[]{0d,40d},elevations=new[]{25f,25f},gradeDerivatives=new[]{0f,0f},crossfalls=new[]{0f,0f},crownRates=new[]{.025f,.025f}}).ToArray();
            b=Bytes(good);var inspected=World13DocumentCodec.ReadCandidate(b,Prepared(b));World13DocumentCodec.CheckFrozenAuthorityForDiagnostics(inspected);Check(!inspected.Admitted,"frozen validators can pass without granting prepared admission");Reject(()=>World13DocumentCodec.DecodePrepared(b,Prepared(b)),"even frozen-valid prepared envelope fails closed");
        }
        static void Adversaries()
        {
            void Bad(Action<World13Document> mutate,string why){var d=Fixture();mutate(d);var b=Bytes(d);Reject(()=>World13DocumentCodec.ReadVerifiedRaw(b,Raw(b)),why);}
            Bad(d=>d.roadAuthority.sourceRoads[1].roadId="r2","duplicate source row rejected");Bad(d=>d.roadAuthority.sourceRoads=d.roadAuthority.sourceRoads.Take(1).ToArray(),"missing road row rejected");Bad(d=>d.roadAuthority.sourceRoads[1].nodeIds=new[]{"11","20"},"wrong source endpoint rejected");Bad(d=>d.roadAuthority.sourceRoads[1].nodeIds=new[]{"10"},"node/point count mismatch rejected");Bad(d=>d.roadAuthority.sourceDigest=Evidence,"tampered source digest rejected");Bad(d=>d.roadAuthority.layoutRecipe="preserved-ways-v1","payload cannot grant different source origin");Bad(d=>d.geography.preparedWidths=true,"legacy prepared width flag cannot become verified raw");Bad(d=>d.geography.roads[0].points[0].y+=.01f,"source float tamper rejected");Bad(d=>d.geography.roads[0].id=null,"null source road id rejected");Bad(d=>d.geography.railwayProfilesPrepared=true,"legacy solved-rail flag cannot be raw");
            Bad(d=>d.geography.roads[0].width+=.1f,"source2 width tamper rejected with fresh documentSHA");Bad(d=>d.geography.roads[0].name="Changed","source2 name tamper rejected");Bad(d=>d.geography.roads[0].kind="service","source2 kind tamper rejected");Bad(d=>d.geography.roads[0].limit=40,"source2 limit tamper rejected");Bad(d=>d.geography.roads[0].oneWay=1,"source2 direction tamper rejected");Bad(d=>d.geography.roads[0].maxVehicleHeight=4,"source2 maxheight tamper rejected");
            var nullName=Fixture();nullName.geography.roads[0].name=null;string nullDigest=SourceDigest(nullName);nullName.geography.roads[0].name="";Check(nullDigest!=SourceDigest(nullName),"source2 optional null string differs from empty");
            var data=Bytes(Fixture());var wrong=World13ReadContext.FromVerifiedRawReceipt(World13ReadContext.RawOrigin.ExplicitAuthoredSource,Compatibility,Evidence,Evidence);Reject(()=>World13DocumentCodec.ReadVerifiedRaw(data,wrong),"captured byte pin mismatch rejected");
            string text=Encoding.UTF8.GetString(data);Reject(()=>World13DocumentCodec.Preflight(WorldFieldJson.Write(Fixture().geography)),"bare geography cannot masquerade as envelope");Reject(()=>World13DocumentCodec.Preflight(text.Replace("\"version\":1","\"version\":1.0")),"float version is not integer");Reject(()=>World13DocumentCodec.Preflight(text.Insert(1,"\"format\":\"duplicate\",")),"duplicate root field rejected");Reject(()=>World13DocumentCodec.Preflight(text.Insert(1,"\"$type\":\"untrusted\",")),"unknown metadata field rejected");Reject(()=>World13DocumentCodec.Preflight(text.Replace("\"width\":1.25","\"sourceNodeIds\":[\"10\",\"20\"],\"width\":1.25")),"flat NS road metadata rejected outside envelope");Reject(()=>World13DocumentCodec.Preflight(text.Replace("\"sourceRoads\":[","\"sourceRoads\":null,\"extra\":[")),"null required node rows rejected before allocation");
            var over=Fixture();over.roadAuthority.elevationProfiles=new[]{new RoadElevationProfileDto{roadId="r1",distances=new double[513],elevations=new float[513],gradeDerivatives=new float[513],crossfalls=new float[513],crownRates=new float[513]}};Reject(()=>World13DocumentCodec.Preflight(Encoding.UTF8.GetString(Bytes(over))),"profile array513 exceeds preallocation cap512");
            var total=Fixture();total.roadAuthority.sourceRoads=Enumerable.Range(0,31).Select(i=>new SourceRoad13Metadata{roadId="over-"+i,nodeIds=Enumerable.Repeat("10",10000).ToArray()}).ToArray();Reject(()=>World13DocumentCodec.Preflight(Encoding.UTF8.GetString(Bytes(total))),"aggregate source-node300000 preallocation cap");
            Reject(()=>World13DocumentCodec.Preflight(text.Replace("\"datum\":0","\"datum\":1e-50")),"nonzero float underflow rejected");Reject(()=>World13DocumentCodec.Preflight(text+" {}"),"trailing document rejected");Reject(()=>World13DocumentCodec.Preflight(text.Substring(0,text.Length-1)),"truncated document rejected");
        }
        public static void Run()
        {
            int exit=1;checks=0;string output=Path.GetFullPath("Builds/Verification/World13Envelope/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=HashFile(career);
            try{Check(!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"isolated no real session");RawTests(output);PrecisionInspection();Adversaries();Check(HashFile(career)==before,"protected real career unchanged");File.WriteAllText(Path.Combine(output,"receipt.json"),"{\"status\":\"PASS\",\"checks\":"+checks+",\"scope\":\"raw13 precise envelope and unadmitted prepared inspection only; no full13/boot/peer/native admission\"}");exit=0;}
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);}
            Debug.Log("WORLD13 ENVELOPE "+(exit==0?"PASS ":"FAIL ")+checks+" "+output);EditorApplication.Exit(exit);
        }
    }
}
