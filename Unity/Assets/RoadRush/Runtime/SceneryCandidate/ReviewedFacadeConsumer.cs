using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoadRush.SceneryCandidate
{
    // OUTSIDE-ONLY candidate. No default source/ground/ownership authority is installed.
    // Entry.Geometry/ContentKey/id are NEVER a provider identity.
    public sealed class SourceBinding
    {
        public string[] SourceIdentity,DirectedNativeEdgeHex;
        public string CanonicalEdgeKey,WorldSha256,FrameSha256,RepresentativeSha256,GeometrySha256,OriginalSourceSha256;
        public int DirectedCurrentEdge;
    }
    public interface IOriginalBindingResolver { bool TryResolve(GeoBuilding original,out SourceBinding binding); }
    public sealed class ReviewedPacket
    {
        // Expected pins come from primary's external authority configuration, not from packet fields.
        public byte[] Bytes; public string ExpectedSha256,ExpectedReviewIndexSha256;
        public bool SyntheticTestOnly;
        public float[] LodScreenHeights; // Explicit reviewed runtime policy; no guessed thresholds.
        public string MaterialPolicySha256,FeaturePolicySha256,ExpectedColliderLifetimeSha256,ExpectedFeatureSnapshotSha256;
    }
    public interface IReviewedPacketProvider { bool TryGet(SourceBinding source,out ReviewedPacket packet); }
    public sealed class MaterialPlan
    {
        public string Id; public float[] Colour; public float Roughness,Metallic;
        public string ShaderPolicy="OPAQUE_SCALAR_PBR_SINGLE_SIDED";
    }
    public sealed class LodMeshPlan
    {
        public int Level; public string ArtifactPath,ArtifactSha256,OutputBufferSha256;
        public float[] Vertices,UV,Normals,Tangents,BoundsMin,BoundsMax;
        public double[] AuthoringPositions64;public int OriginalPositionCount;
        public string[] MaterialIds; public int[][] SubmeshIndices;
    }
    public sealed class ModuleAsset
    {
        public string Id; public string[] SourceIdentity;
        public double[] StartXZ,EndXZ,OutwardXZ;
        public MaterialPlan[] Materials; public LodMeshPlan[] Lods;
    }
    public interface IModuleAssets { bool TryGet(string moduleId,out ModuleAsset asset); }
    public sealed class ExistingBody
    {
        // Logical handles supplied by actual owner, NEVER guessed from geometry or object names.
        public string BodyId,BodyOwner,ShellOwner,FacadeOwner,ColliderOwner;
        public string[] ColliderIds,RoofChildIds;
        public string ColliderLifetimeSha256,FeatureSnapshotSha256,PublicationPolicy;
        public bool OriginalColliderMeshRetainedBySeparateLifetimeOwner;
        public bool FunctionalFeatureInventoryComplete;public bool MeshSpaceIsCurrentWorldLocalIdentity;
        public ProtectedFeature[] ProtectedFeatures=Array.Empty<ProtectedFeature>();
    }
    public sealed class ProtectedFeature
    {
        // Reviewed edge + along-edge/Y rectangle, including doors/stairs/garage/access visuals.
        public int Edge;public float AlongMin,AlongMax,YMin,YMax; public string Kind,Id;
    }
    public sealed class WindowSuppression
    {
        public string Id; public int Edge; // IDs are dedup handles only, not source identity.
    }
    public sealed class FacadePlan
    {
        public string BodyId,CanonicalEdgeKey,ReviewPacketSha256,FeaturePolicySha256,MaterialPolicySha256,ModuleId,FacadeRendererBindingId;
        public string WorldSha256,FrameSha256,CurrentRepresentativeSha256,ReviewIndexSha256,BodyOwner,ShellOwner,FacadeOwner,ColliderOwner;
        public Dictionary<string,string> ReviewedReceiptHashes;
        public string[] SourceIdentity,RetainedColliderIds,RetainedRoofChildIds,RetiredVisualIds;
        public GeoBuilding[] OriginalSources;public string[] OriginalSourceCommits;public GeoBuilding RenderGeometry;
        public string GeometrySha256,ColliderLifetimeSha256,FeatureSnapshotSha256,OutputCommitSha256,CosmeticPlacementPolicy;
        public string[] RawCurrentEdgeXZBits,EmittedCosmeticEdgeXZBits;public double[] EmittedEndpointDisplacementMetres;
        public int Edge;public float[] MatrixRowMajor;public float FloorY,BandMinY,BandMaxY;
        public MaterialPlan[] Materials;public LodMeshPlan[] Lods;public float[] LodScreenHeights;
        public WindowSuppression[] SuppressWindows;
        public bool SyntheticTestOnly;
        public bool RuntimeActivationAllowed=>false; // Separate primary transaction grant is mandatory.
    }
    public sealed class Decision
    {
        public string Reason; public FacadePlan Plan;
        public bool CandidateReady=>Plan!=null;
        public static Decision Fallback(string reason)=>new Decision{Reason=reason};
    }
    public static class ReviewedFacadeConsumer
    {
        static void Require(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
        public static string Sha(byte[] bytes){using(var sha=SHA256.Create())return Hex(sha.ComputeHash(bytes));}
        public static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        public static bool IsSha(string text)=>text!=null&&text.Length==64&&text.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static string F(float f)=>Hex(BitConverter.GetBytes(f));
        static string XZ(Vector3 p)=>F(p.x)+F(p.z);
        static bool Same(string[] a,string[] b)=>a!=null&&b!=null&&a.Length==b.Length&&a.SequenceEqual(b,StringComparer.Ordinal);
        public static JObject Parse(byte[] raw)
        {
            Require(raw!=null&&raw.Length<=4*1024*1024,"PACKET_RESOURCE_CAP");
            using(var sr=new StringReader(new UTF8Encoding(false,true).GetString(raw)))
            using(var jr=new JsonTextReader(sr){MaxDepth=32,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})
            {var o=JObject.Load(jr,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});Require(!jr.Read(),"TRAILING_JSON");return o;}
        }
        // Source-bound review must use this versioned consumer commit for current normalized geometry.
        // Geometry.id and ContentKey are intentionally excluded; original refs remain a separate binding.
        public static string GeometryCommit(GeoBuilding b)
        {
            Require(b!=null&&b.points!=null&&b.points.Length>=4&&b.points.Length<=257,"GEOMETRY_BUDGET");
            using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms,new UTF8Encoding(false,true),true))
            {
                w.Write(Encoding.ASCII.GetBytes("RoadRushConsumerGeometry1\0"));
                foreach(var text in new[]{b.kind,b.roof}){w.Write(text!=null);if(text!=null){var bytes=Encoding.UTF8.GetBytes(text);Require(bytes.Length<=4096,"STRING_BUDGET");w.Write(bytes.Length);w.Write(bytes);}}
                Require(Finite(b.height),"NONFINITE_HEIGHT");w.Write(b.height);w.Write(b.measuredHeight);w.Write(b.points.Length);
                foreach(var p in b.points){Require(Finite(p.x)&&Finite(p.y)&&Finite(p.z),"NONFINITE_POINT");w.Write(p.x);w.Write(p.y);w.Write(p.z);}w.Flush();return Sha(ms.ToArray());
            }
        }
        static string[] Strings(JToken t)=>t==null?null:t.ToObject<string[]>();
        static float[] Floats(JToken t,int count)
        {var a=t?.ToObject<float[]>();Require(a!=null&&a.Length==count&&a.All(Finite),"FLOAT_BUFFER");return a;}
        static float[] FromHex(string value,int count)
        {Require(value!=null&&value.Length==count*8&&value.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'),"FLOAT_HEX");var result=new float[count];for(int i=0;i<count;i++){byte[] bytes=new byte[4];for(int j=0;j<4;j++)bytes[j]=Convert.ToByte(value.Substring(i*8+j*2,2),16);result[i]=BitConverter.ToSingle(bytes,0);}Require(result.All(Finite),"NONFINITE_FLOAT_HEX");return result;}
        static bool Near(double a,double b,double eps=1e-6)=>Math.Abs(a-b)<=eps;
        static void Rigid(float[] m)
        {
            foreach(var p in new[]{(1,0f),(4,0f),(5,1f),(6,0f),(9,0f),(12,0f),(13,0f),(14,0f),(15,1f)})Require(Near(m[p.Item1],p.Item2),"TILT_OR_PROJECTIVE");
            double a=m[0],b=m[2],c=m[8],d=m[10];Require(Near(a*a+c*c,1)&&Near(b*b+d*d,1)&&Near(a*b+c*d,0)&&Near(a*d-b*c,1),"SCALE_OR_MIRROR");
        }
        // Explicit binary32 rounding through byte round-trip forbids JIT FMA/reassociation.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        static float Round(float x)=>BitConverter.ToSingle(BitConverter.GetBytes(x),0);
        public static float Placed(float a,float x,float b,float z,float offset)=>Round(Round(Round(a*x)+Round(b*z))+offset);
        static string MappedXZ(float[] m,double[] p)=>F(Placed(m[0],(float)p[0],m[2],(float)p[1],m[3]))+F(Placed(m[8],(float)p[0],m[10],(float)p[1],m[11]));
        public static string OriginalSourceCommit(GeoBuilding b)
        {
            // Bind the original pre-normalization reference to its reviewed content, including local ID.
            Require(b!=null&&b.points!=null&&b.points.Length>=3&&b.points.Length<=4096,"ORIGINAL_SOURCE_BUDGET");
            using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms,Encoding.UTF8,true))
            {w.Write("RoadRushConsumerOriginal1");foreach(var text in new[]{b.id,b.kind,b.roof}){w.Write(text!=null);if(text!=null)w.Write(text);}w.Write(b.height);w.Write(b.measuredHeight);w.Write(b.points.Length);foreach(var q in b.points){w.Write(q.x);w.Write(q.y);w.Write(q.z);}w.Flush();return Sha(ms.ToArray());}
        }
        public static LodMeshPlan Bake(LodMeshPlan source,float[] m)
        {
            var v=(float[])source.Vertices.Clone();var n=(float[])source.Normals.Clone();var t=(float[])source.Tangents.Clone();
            for(int i=0;i<v.Length;i+=3){float x=v[i],z=v[i+2];v[i]=Placed(m[0],x,m[2],z,m[3]);v[i+1]=Round(v[i+1]+m[7]);v[i+2]=Placed(m[8],x,m[10],z,m[11]);x=n[i];z=n[i+2];n[i]=Placed(m[0],x,m[2],z,0);n[i+2]=Placed(m[8],x,m[10],z,0);int j=i/3*4;x=t[j];z=t[j+2];t[j]=Placed(m[0],x,m[2],z,0);t[j+2]=Placed(m[8],x,m[10],z,0);}
            var low=new float[3];var high=new float[3];for(int axis=0;axis<3;axis++){low[axis]=v.Where((x,i)=>i%3==axis).Min();high[axis]=v.Where((x,i)=>i%3==axis).Max();}
            var result=new LodMeshPlan{OriginalPositionCount=source.OriginalPositionCount,AuthoringPositions64=(double[])source.AuthoringPositions64.Clone(),Level=source.Level,ArtifactPath=source.ArtifactPath,ArtifactSha256=source.ArtifactSha256,Vertices=v,UV=(float[])source.UV.Clone(),Normals=n,Tangents=t,BoundsMin=low,BoundsMax=high,MaterialIds=(string[])source.MaterialIds.Clone(),SubmeshIndices=source.SubmeshIndices.Select(a=>(int[])a.Clone()).ToArray()};result.OutputBufferSha256=BufferCommit(result);return result;
        }
        public static string BufferCommit(LodMeshPlan lod)
        {
            using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms,Encoding.UTF8,true))
            {w.Write("RoadRushPlacedFacadeMesh1");w.Write(lod.Level);foreach(var a in new[]{lod.Vertices,lod.UV,lod.Normals,lod.Tangents,lod.BoundsMin,lod.BoundsMax}){w.Write(a.Length);foreach(float x in a)w.Write(x);}w.Write(lod.MaterialIds.Length);foreach(var id in lod.MaterialIds)w.Write(id);w.Write(lod.SubmeshIndices.Length);foreach(var a in lod.SubmeshIndices){w.Write(a.Length);foreach(int x in a)w.Write(x);}w.Flush();return Sha(ms.ToArray());}
        }
        static double Area(Vector3[] ring){double area=0;for(int i=0;i<ring.Length-1;i++)area+=(double)ring[i].x*ring[i+1].z-(double)ring[i+1].x*ring[i].z;return area;}
        public static Decision TryPlan(BuildingRenderInput.Plan inputs,GeoBuilding section,GeoBuilding source,
            BuildingSitePlan.Data site,IOriginalBindingResolver bindings,IReviewedPacketProvider reviews,IModuleAssets assets,
            ExistingBody existing,IReadOnlyList<BuildingFacadePlan.Opening> openings)
        {
            try
            {
                BuildingRenderInput.Entry entry=null;Require(inputs!=null&&inputs.TryGet(source,out entry)&&!entry.Skipped,"UNKNOWN_RENDER_INPUT");
                Require(ReferenceEquals(section,entry.Geometry)&&ReferenceEquals(source,entry.Geometry),"SPLIT_SECTION_UNSUPPORTED");
                Require(site!=null&&!site.HighRelief&&!site.RoofBurialRisk&&!site.ConservativeFallback&&!site.RequiresAccessReview,"SITE_OR_ACCESS_REVIEW_REQUIRED");
                Require(bindings!=null&&reviews!=null&&assets!=null,"NO_REVIEW_SERVICES");
                Require(entry.OriginalSources!=null&&entry.OriginalSources.Length>0&&entry.OriginalSources.Length<=64,"ORIGINAL_SOURCE_BUDGET");
                var commit=GeometryCommit(entry.Geometry);SourceBinding bound=null;
                foreach(var original in entry.OriginalSources)
                {
                    SourceBinding found=null;Require(original!=null&&bindings.TryResolve(original,out found)&&found!=null,"UNBOUND_ORIGINAL_SOURCE");
                    Require(found.SourceIdentity!=null&&found.SourceIdentity.Length==4&&found.SourceIdentity.All(s=>!string.IsNullOrEmpty(s)),"INCOMPLETE_CANONICAL_IDENTITY");
                    Require(found.GeometrySha256==commit,"CURRENT_GEOMETRY_CHANGED");
                    Require(found.OriginalSourceSha256==OriginalSourceCommit(original),"ORIGINAL_SOURCE_CHANGED_AFTER_BINDING");
                    if(bound==null)bound=found;else Require(Same(bound.SourceIdentity,found.SourceIdentity)&&bound.CanonicalEdgeKey==found.CanonicalEdgeKey&&bound.DirectedCurrentEdge==found.DirectedCurrentEdge&&Same(bound.DirectedNativeEdgeHex,found.DirectedNativeEdgeHex)&&bound.WorldSha256==found.WorldSha256&&bound.FrameSha256==found.FrameSha256&&bound.RepresentativeSha256==found.RepresentativeSha256,"COALESCED_SOURCE_BINDING_CONFLICT");
                }
                Require(reviews.TryGet(bound,out var packet)&&packet!=null,"NO_REVIEWED_PACKET");
                Require(IsSha(packet.ExpectedSha256)&&Sha(packet.Bytes)==packet.ExpectedSha256&&IsSha(packet.ExpectedReviewIndexSha256),"EXTERNAL_PACKET_PIN");
                var root=Parse(packet.Bytes);string status=(string)root["status"];Require((string)root["gateRevision"]=="1.2","GATE_RAW_COHERENCE_REVISION_REQUIRED");
                Require(packet.SyntheticTestOnly?status=="PASS_SYNTHETIC_DRY_ONLY":status=="PASS_REVIEWED_DRY_COMPOSITION_ONLY","REVIEW_SCOPE");
                Require(root["runtimeActivationAllowed"]?.Type==JTokenType.Boolean&&!(bool)root["runtimeActivationAllowed"]&&root["placementPerformed"]?.Type==JTokenType.Boolean&&!(bool)root["placementPerformed"]&&root["sourceOrSceneModified"]?.Type==JTokenType.Boolean&&!(bool)root["sourceOrSceneModified"],"NOT_A_PURE_DRY_PROOF");
                var p=root["dryPlan"] as JObject;Require(p!=null&&(string)p["schema"]=="RoadRushReviewedDryCompositionPlan1"&&p["runtimeActivationAllowed"]?.Type==JTokenType.Boolean&&!(bool)p["runtimeActivationAllowed"],"DRY_PLAN_SCHEMA");
                Require((string)p["reviewIndexSha256"]==packet.ExpectedReviewIndexSha256&&(string)root["reviewIndexSha256"]==packet.ExpectedReviewIndexSha256,"INDEX_PIN");
                Require(Same(Strings(p["sourceIdentity"]),bound.SourceIdentity)&&(string)p["canonicalEdgeKey"]==bound.CanonicalEdgeKey&&Same(Strings(p["directedNativeEdgeHex"]),bound.DirectedNativeEdgeHex),"SOURCE_NATIVE_EDGE_MISMATCH");
                Require((string)p["worldSha256"]==bound.WorldSha256&&(string)p["frameSha256"]==bound.FrameSha256&&(string)p["currentRepresentativeSha256"]==bound.RepresentativeSha256&&(string)p["currentRenderGeometrySha256"]==commit,"CURRENT_WORLD_OR_GEOMETRY_MISMATCH");
                Require(assets.TryGet((string)p["moduleId"],out var asset)&&asset!=null&&Same(asset.SourceIdentity,bound.SourceIdentity),"UNKNOWN_OR_WRONG_MODULE");
                Require(asset.Lods!=null&&asset.Lods.Length==3&&asset.Lods.Select(x=>x.Level).SequenceEqual(new[]{0,1,2})&&Same(asset.Lods.Select(x=>x.ArtifactSha256).ToArray(),Strings(p["artifactHashes"])),"LOD_ARTIFACT_MAP");
                Require(packet.LodScreenHeights!=null&&packet.LodScreenHeights.Length==3&&packet.LodScreenHeights.All(x=>Finite(x)&&x>0&&x<=1)&&packet.LodScreenHeights[0]>packet.LodScreenHeights[1]&&packet.LodScreenHeights[1]>packet.LodScreenHeights[2],"NO_REVIEWED_LOD_POLICY");
                Require(IsSha(packet.MaterialPolicySha256)&&IsSha(packet.FeaturePolicySha256),"MATERIAL_OR_FEATURE_POLICY_UNREVIEWED");
                int edge=bound.DirectedCurrentEdge;var ring=section.points;Require(edge>=0&&edge<ring.Length-1,"CURRENT_EDGE_RANGE");
                var edgeBits=new[]{XZ(ring[edge]),XZ(ring[edge+1])};Require(Same(edgeBits,Strings(p["currentDirectedEdgeXZBits"]))&&edgeBits[0]!=edgeBits[1],"CURRENT_DIRECTED_EDGE_MISMATCH");
                Require((string)p["placementOperation"]=="F32_SEPARATE_MUL_ADD_XZ_1","UNSUPPORTED_PLACEMENT_OPERATION");
                var matrix=FromHex((string)p["matrixF32LEHex"],16);Rigid(matrix);
                var policy=(string)p["cosmeticPlacementPolicy"];Require(policy=="EXACT_CURRENT_ENDPOINT_F32_V1"||policy=="F32_COSMETIC_WORLD_DISPLACEMENT_1MM_V1","UNREVIEWED_COSMETIC_POLICY");
                var emitted=new[]{MappedXZ(matrix,asset.StartXZ),MappedXZ(matrix,asset.EndXZ)};Require(Same(emitted,Strings(p["emittedCosmeticEdgeXZBits"])),"CANONICAL_EMITTED_ANCHOR_MISMATCH");
                var errors=new double[2];var ledger=p["emittedEndpointDisplacementMetres"]?.ToObject<double[]>();Require(ledger!=null&&ledger.Length==2,"MISSING_ACTUAL_REPLAY_LEDGER");
                for(int j=0;j<2;j++){var actual=FromHex(emitted[j],2);var expected=FromHex(edgeBits[j],2);double dxError=(double)actual[0]-expected[0],dzError=(double)actual[1]-expected[1];errors[j]=Math.Sqrt(dxError*dxError+dzError*dzError);Require(!double.IsNaN(ledger[j])&&!double.IsInfinity(ledger[j])&&Near(errors[j],ledger[j],1e-12),"REPLAY_LEDGER_MISMATCH");}
                Require(policy=="EXACT_CURRENT_ENDPOINT_F32_V1"?Same(emitted,edgeBits):errors.All(x=>x<=.001),"REQUIRES_REVIEWED_PARAMETRIC_REGENERATION");
                Require((double)p["maximumCosmeticDisplacementMetres"]==(policy=="EXACT_CURRENT_ENDPOINT_F32_V1"?0:.001),"COSMETIC_BUDGET_MISMATCH");
                double dx=(double)ring[edge+1].x-ring[edge].x,dz=(double)ring[edge+1].z-ring[edge].z,len=Math.Sqrt(dx*dx+dz*dz),sign=Area(ring)>=0?1:-1;
                Require(len>.01,"EDGE_DEGENERATE");var normal=Floats(p["mappedOutwardNormalXZ"],2);double nx=dz/len*sign,nz=-dx/len*sign;
                Require(Near(normal[0],nx)&&Near(normal[1],nz)&&Near(matrix[0]*asset.OutwardXZ[0]+matrix[2]*asset.OutwardXZ[1],nx)&&Near(matrix[8]*asset.OutwardXZ[0]+matrix[10]*asset.OutwardXZ[1],nz),"DIRECTED_OUTWARD_NORMAL_MISMATCH");
                float floor=FromHex((string)p["floorF32LEHex"],1)[0];var band=FromHex((string)p["bandF32LEHex"],2);
                Require(F(floor)==F(site.LevelFloorY)&&band[0]>=floor&&band[1]>band[0]&&Near(band[1]-band[0],2.8,2e-5)&&band[1]<=floor+section.height&&F(matrix[7])==F(band[0]),"CURRENT_FLOOR_OR_BAND_MISMATCH");
                Require(existing!=null&&existing.BodyId==(string)p["bodyId"],"EXISTING_BODY_MISMATCH");var owners=p["owners"];
                Require(existing.BodyOwner==(string)owners?["body"]&&existing.ShellOwner==(string)owners?["shell"]&&existing.FacadeOwner==(string)owners?["facade"]&&existing.ColliderOwner==(string)owners?["collider"],"OWNER_MISMATCH");
                Require(existing.ColliderIds!=null&&existing.ColliderIds.Length>0&&existing.ColliderIds.Distinct().Count()==existing.ColliderIds.Length&&Same(existing.ColliderIds,Strings(p["retainedColliderIds"])),"BODY_COLLIDER_RETENTION_MISMATCH");
                Require(existing.PublicationPolicy=="REVIEWED_UNPUBLISHED_CONSTRUCTION","LIVE_OR_UNKNOWN_CHUNK_UNSUPPORTED");
                Require(existing.MeshSpaceIsCurrentWorldLocalIdentity,"MESH_SPACE_NEEDS_REVIEWED_ADAPTER");
                Require(existing.OriginalColliderMeshRetainedBySeparateLifetimeOwner&&IsSha(existing.ColliderLifetimeSha256)&&existing.ColliderLifetimeSha256==packet.ExpectedColliderLifetimeSha256,"COLLIDER_MESH_LIFETIME_NOT_RETAINED");
                Require(existing.FunctionalFeatureInventoryComplete&&IsSha(existing.FeatureSnapshotSha256)&&existing.FeatureSnapshotSha256==packet.ExpectedFeatureSnapshotSha256&&existing.ProtectedFeatures!=null&&existing.RoofChildIds!=null&&openings!=null&&openings.Count<=192,"FUNCTIONAL_FEATURE_INVENTORY_REQUIRED");
                foreach(var f in existing.ProtectedFeatures){Require(f!=null&&f.Edge>=0&&Finite(f.AlongMin)&&Finite(f.AlongMax)&&f.AlongMax>=f.AlongMin&&Finite(f.YMin)&&Finite(f.YMax)&&f.YMax>=f.YMin,"MALFORMED_PROTECTED_FEATURE");if(f.Edge==edge&&f.AlongMax>0&&f.AlongMin<len&&f.YMax>band[0]&&f.YMin<band[1])throw new InvalidDataException("FUNCTIONAL_FEATURE_CONFLICT");}
                Require(!Strings(p["retiredVisualBindingIds"]).Contains((string)p["facadeRendererBindingId"]),"RETIRED_NEW_RENDERER_ALIAS");
                var suppressed=new List<WindowSuppression>();var ids=new HashSet<string>(StringComparer.Ordinal);
                foreach(var opening in openings)
                {
                    Require(!string.IsNullOrEmpty(opening.Id)&&ids.Add(opening.Id)&&Finite(opening.Position.x)&&Finite(opening.Position.y)&&Finite(opening.Position.z)&&Finite(opening.Width)&&Finite(opening.Height)&&opening.Width>0&&opening.Height>0,"MALFORMED_OPENING");
                    double x=opening.Position.x-ring[edge].x,z=opening.Position.z-ring[edge].z,along=(x*dx+z*dz)/len,away=x*nx+z*nz;
                    double lo=opening.Position.y-opening.Height/2,hi=opening.Position.y+opening.Height/2;
                    if(Math.Abs(away)>.08||along+opening.Width/2<=0||along-opening.Width/2>=len||hi<=band[0]||lo>=band[1])continue;
                    Require(!opening.Door,"EXISTING_DOOR_CONFLICT");
                    Require(along-opening.Width/2>=0&&along+opening.Width/2<=len&&lo>=band[0]&&hi<=band[1],"WINDOW_STRADDLES_ACCEPTED_BAND");
                    // Quaternion forward checked without Unity native calls; prevents a rear/different edge match.
                    var q=opening.Rotation;double fx=2*(q.x*q.z+q.w*q.y),fz=1-2*(q.x*q.x+q.y*q.y);
                    Require(Near(fx,nx,1e-4)&&Near(fz,nz,1e-4),"WINDOW_FRAME_MISMATCH");
                    suppressed.Add(new WindowSuppression{Id=opening.Id,Edge=edge});
                }
                var baked=asset.Lods.Select(l=>Bake(l,matrix)).ToArray();
                string matrix64=(string)p["reviewedMatrixF64LEHex"];Require(matrix64!=null&&matrix64.Length==256,"REVIEWED_MATRIX64_REQUIRED");var md=new double[16];
                for(int j=0;j<16;j++){var bytes=new byte[8];for(int k=0;k<8;k++)bytes[k]=Convert.ToByte(matrix64.Substring(j*16+k*2,2),16);md[j]=BitConverter.ToDouble(bytes,0);Require(!double.IsNaN(md[j])&&!double.IsInfinity(md[j])&&F((float)md[j])==F(matrix[j]),"REVIEWED_EMITTED_MATRIX_MISMATCH");}
                var meshLedger=p["emittedMeshReplayLedger"] as JArray;Require(meshLedger!=null&&meshLedger.Count==3,"MESH_REPLAY_LEDGER_REQUIRED");
                for(int level=0;level<3;level++)
                {
                    var l=baked[level];double maximum=0;Require(l.AuthoringPositions64!=null&&l.AuthoringPositions64.Length==l.Vertices.Length,"AUTHORING_POSITION_TRACE");
                    for(int j=0;j<l.Vertices.Length;j+=3){double x=l.AuthoringPositions64[j],y=l.AuthoringPositions64[j+1],z=l.AuthoringPositions64[j+2],ex=md[0]*x+md[2]*z+md[3],ey=y+md[7],ez=md[8]*x+md[10]*z+md[11],dxError=l.Vertices[j]-ex,dyError=l.Vertices[j+1]-ey,dzError=l.Vertices[j+2]-ez;maximum=Math.Max(maximum,Math.Sqrt(dxError*dxError+dyError*dyError+dzError*dzError));}
                    Require((int)meshLedger[level]["level"]==level&&(int)meshLedger[level]["vertices"]==l.OriginalPositionCount&&maximum<=.001&&Near(maximum,(double)meshLedger[level]["maximumWorldDisplacementMetres"],1e-12),"MESH_WORLD_REPLAY_LEDGER_MISMATCH");
                }
                var outputCommit=Sha(Encoding.ASCII.GetBytes(string.Join("",baked.Select(l=>l.OutputBufferSha256))));
                return new Decision{Reason="READY_FOR_REVIEWED_OWNER_TRANSACTION",Plan=new FacadePlan{WorldSha256=bound.WorldSha256,FrameSha256=bound.FrameSha256,CurrentRepresentativeSha256=bound.RepresentativeSha256,ReviewIndexSha256=packet.ExpectedReviewIndexSha256,ReviewedReceiptHashes=p["receiptHashes"].ToObject<Dictionary<string,string>>(),BodyOwner=existing.BodyOwner,ShellOwner=existing.ShellOwner,FacadeOwner=existing.FacadeOwner,ColliderOwner=existing.ColliderOwner,ModuleId=asset.Id,FacadeRendererBindingId=(string)p["facadeRendererBindingId"],BodyId=existing.BodyId,SourceIdentity=(string[])bound.SourceIdentity.Clone(),CanonicalEdgeKey=bound.CanonicalEdgeKey,OriginalSources=(GeoBuilding[])entry.OriginalSources.Clone(),OriginalSourceCommits=entry.OriginalSources.Select(OriginalSourceCommit).ToArray(),RenderGeometry=entry.Geometry,GeometrySha256=commit,ReviewPacketSha256=packet.ExpectedSha256,MaterialPolicySha256=packet.MaterialPolicySha256,FeaturePolicySha256=packet.FeaturePolicySha256,ColliderLifetimeSha256=existing.ColliderLifetimeSha256,FeatureSnapshotSha256=existing.FeatureSnapshotSha256,Edge=edge,MatrixRowMajor=matrix,FloorY=floor,BandMinY=band[0],BandMaxY=band[1],CosmeticPlacementPolicy=policy,RawCurrentEdgeXZBits=edgeBits,EmittedCosmeticEdgeXZBits=emitted,EmittedEndpointDisplacementMetres=errors,OutputCommitSha256=outputCommit,Materials=asset.Materials,Lods=baked,LodScreenHeights=(float[])packet.LodScreenHeights.Clone(),RetainedColliderIds=(string[])existing.ColliderIds.Clone(),RetainedRoofChildIds=(string[])existing.RoofChildIds.Clone(),RetiredVisualIds=Strings(p["retiredVisualBindingIds"]),SuppressWindows=suppressed.ToArray(),SyntheticTestOnly=packet.SyntheticTestOnly}};
            }
            catch(Exception e) when(e is InvalidDataException||e is JsonException||e is ArgumentException||e is FormatException||e is OverflowException||e is NullReferenceException||e is IndexOutOfRangeException){return Decision.Fallback(e.Message);}
        }
    }
    // Implemented by primary's shell/body owner, not this adapter. Prepare stages hidden NEW visual
    // data only; original renderer/collider/roof remain intact. Commit is atomic and Rollback restores
    // original visual band. Do NOT destroy/disable the wall object or release its collider mesh.
    public interface IFacadeOwnerTransaction : IDisposable
    {
        bool Prepare(FacadePlan plan); bool Commit(); void Rollback();
        bool OriginalBodyColliderRoofAndAccessPreserved(FacadePlan plan);
    }
    public interface IFacadeOwner
    {
        bool TryBegin(FacadePlan plan,out IFacadeOwnerTransaction transaction);
        bool ActivationGrantedByPrimary(FacadePlan plan);
    }
    public sealed class FacadeRecoveryException : Exception {public FacadeRecoveryException(string message,Exception inner):base(message,inner){}}
    public static class ReviewedFacadeTransaction
    {
        public static bool TryApply(Decision decision,IFacadeOwner owner,bool testHarness=false)
        {
            if(decision?.Plan==null||owner==null||decision.Plan.SyntheticTestOnly!=testHarness||(!testHarness&&!owner.ActivationGrantedByPrimary(decision.Plan)))return false;
            // Revalidate mutable source/mesh references immediately before owner staging.
            try
            {
                var p=decision.Plan;
                if(ReviewedFacadeConsumer.GeometryCommit(p.RenderGeometry)!=p.GeometrySha256||p.OriginalSources.Length!=p.OriginalSourceCommits.Length)return false;
                for(int i=0;i<p.OriginalSources.Length;i++)if(ReviewedFacadeConsumer.OriginalSourceCommit(p.OriginalSources[i])!=p.OriginalSourceCommits[i])return false;
                if(p.Lods.Any(l=>ReviewedFacadeConsumer.BufferCommit(l)!=l.OutputBufferSha256))return false;
            }
            catch{return false;}
            IFacadeOwnerTransaction transaction=null;bool committed=false;
            try
            {
                if(!owner.TryBegin(decision.Plan,out transaction)||transaction==null)return false;
                if(!transaction.Prepare(decision.Plan)||!transaction.OriginalBodyColliderRoofAndAccessPreserved(decision.Plan))return false;
                if(!transaction.Commit()||!transaction.OriginalBodyColliderRoofAndAccessPreserved(decision.Plan))return false;
                committed=true;return true;
            }
            catch{return false;}
            finally
            {
                if(transaction!=null)
                {
                    try{if(!committed)transaction.Rollback();}
                    catch(Exception e){throw new FacadeRecoveryException("Owner rollback contract fault; quarantine registration; restoration is unproven",e);}
                    finally{try{transaction.Dispose();}catch(Exception e){throw new FacadeRecoveryException("Owner lifetime disposal contract fault; quarantine registration",e);}}
                }
            }
        }
    }
}
