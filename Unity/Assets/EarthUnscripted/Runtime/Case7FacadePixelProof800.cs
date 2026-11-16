#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core800;
using EarthUnscripted.TerrainDraft.Core800;
using EarthUnscripted.Scenery800.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EarthUnscripted
{
    /// <summary>
    /// Explicit actorless pixel diagnostic over the genuine curated Case7 factory.
    /// No normal boot, actor creation, motion, collider/raster/material modifications
    /// or substitute issuer. Native buffers are checked against the actual plan;
    /// supplementary full-face physics proof is deliberately not repeated here.
    /// The caller owns an isolated Editor process, source/input/protected-state
    /// binding and hard process deadline. No result establishes visible pixels.
    /// </summary>
    public sealed class Case7FacadePixelProof800 : MonoBehaviour
    {
        public const string Recipe = "ReviewedCase7R2/ActualFacadeSubmissionPixelDiagnostic/1";
        public const string ClearCameraName = "Derived Case7 early Base clear camera";
        public const string OutputPrefix = "/private/tmp/rr019-case7-facade-pixels-";
        const double TimeoutSeconds = 600;
        const int Width = 1920, Height = 1080, ExpectedPackets = 1064;
        const int ExpectedVertices = 322356, ExpectedTriangles = 541479;
        const long MaximumJsonBytes = 32 * 1024 * 1024;
        static readonly int[] TargetIndices = { 1697, 1005 };
        static readonly string[] TargetSourceIds = {
            "os-F8B1F936-0537-449A-B9E4-90DDA82669AE-p72221e4a90078e5ea19c-0",
            "os-87A8F192-726B-484D-8117-51F624AD46E8-pfc6fbd1655527f5792f6-0" };
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly List<GameObject> owned = new List<GameObject>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly List<string> errors = new List<string>();
        readonly JObject receipt = new JObject();
        readonly JArray views = new JArray();
        UploadedGroundBuild800 ground;
        PreparedWorldMeshPlan800 plan;
        Task<ReviewedDerivedInspector800> preparationWork;
        GeoWorld town;
        Material road, terrain;
        Camera camera;
        RenderTexture target;
        Case7FacadeCollector800 collector;
        Case7FacadeCollector800.Mode mode;
        Action<bool, string> completed;
        string output, context, regional;
        double began;
        float originalScale;
        int renderedFrame = -1;
        bool started, ending, completionCalled, sourceCurrent = true;

        public void Begin(string ownedOutput, string reviewedContext, string reviewedRegional,
            Case7FacadeCollector800.Mode observationMode, Action<bool, string> callback)
        {
            Need(!started && Application.isPlaying && GameSession.SuppressAutomaticLaunch
                && !string.IsNullOrEmpty(PlayerProfile.StorageOverride)
                && !FindAnyObjectByType<GameSession>() && callback != null,
                "Fresh isolated actorless PlayMode required");
            Need(Path.GetFullPath(ownedOutput) == ownedOutput && ownedOutput.StartsWith(OutputPrefix, StringComparison.Ordinal)
                && Directory.Exists(ownedOutput) && !File.Exists(Path.Combine(ownedOutput, "receipt.json")), "Fresh claimed private output");
            Need(observationMode == Case7FacadeCollector800.Mode.Full || observationMode == Case7FacadeCollector800.Mode.None,"Explicit supported observation mode");
            started = true; output = ownedOutput; context = reviewedContext; regional = reviewedRegional;
            mode = observationMode; completed = callback; began = Time.realtimeSinceStartupAsDouble;
            originalScale = Time.timeScale;
            Application.logMessageReceived += Logged;
            RenderPipelineManager.endCameraRendering += Rendered;
            receipt["recipe"] = Recipe; receipt["mode"] = mode.ToString();
            receipt["visualReviewPending"] = true; receipt["pixelCauseProved"] = false;
            receipt["motionAdmitted"] = false; receipt["supportedPathIntervalsIssued"] = false;
            receipt["supplementaryPhysicsRaysRepeated"] = false;
            receipt["nativeBufferHashSchema"] = "LengthPrefixedLocalF32BitsAndI32Indices/1";
            receipt["physicsAcceptance"] = "Not established by this pixel diagnostic; preserve separate historical0354 evidence";
            StartCoroutine(Guard(Inspect()));
        }
        static void Need(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException("CASE7 FACADE PIXELS: " + reason); }
        void Logged(string message, string stack, LogType type)
        { if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 32) errors.Add(message + "\n" + stack); }
        void Rendered(ScriptableRenderContext _, Camera actual)
        { if (actual == camera) renderedFrame = Time.frameCount; }
        void Update()
        { if (started && !ending && Time.realtimeSinceStartupAsDouble - began > TimeoutSeconds) Finish(new TimeoutException("600-second pixel watchdog")); }
        GameObject Own(string name)
        { var value = new GameObject(name); owned.Add(value); return value; }
        IEnumerator Guard(IEnumerator work)
        {
            try
            {
                while (!ending)
                {
                    bool more = false; object next = null; Exception failure = null;
                    try { more = work.MoveNext(); if (more) next = work.Current; } catch (Exception error) { failure = error; }
                    if (failure != null) { Finish(failure); yield break; }
                    if (!more) yield break;
                    if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
                }
            }
            finally { try { (work as IDisposable)?.Dispose(); } catch (Exception error) { Finish(error); } }
        }
        IEnumerator Inspect()
        {
            var early = GameObject.Find(ClearCameraName);
            Need(early && early.GetComponent<Camera>() && early.GetComponent<Camera>().cullingMask == 0,
                "Actual early clear camera exists before first async yield");
            owned.Add(early); Time.timeScale = 0;
            Need(FindObjectsByType<Rigidbody>().Length == 0, "No moving actors");
            var token = cancellation.Token;
            string capturedContext = context, capturedRegional = regional;
            var preparation = Task.Run(() => {
                var input = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(capturedContext, token);
                return ReviewedDerivedCore800Request.CaptureReviewedCase7(input, capturedRegional, token).LoadInspector(token);
            }, token);
            preparationWork = preparation;
            _ = preparation.ContinueWith(task => { var ignored = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            while (!preparation.IsCompleted) yield return null;
            var prepared = preparation.GetAwaiter().GetResult(); plan = prepared.Plan;
            Need(!prepared.MotionAdmitted && !prepared.SupportedPathIntervalsIssued
                && plan.FinalBufferDigest == ReviewedDerivedCase7Policy.FinalDigest
                && plan.PacketCount == ExpectedPackets && plan.VertexCount == ExpectedVertices
                && plan.TriangleCount == ExpectedTriangles, "Exact curated actorless derived artifact");
            foreach (var item in new Dictionary<string,string> { {"rawDocumentDigest",plan.RawDocumentDigest},
                {"sourceDigest",plan.SourceDigest},{"controlDigest",plan.ControlDigest},
                {"finalBufferDigest",plan.FinalBufferDigest},{"ownerLedgerDigest",plan.OwnerLedgerDigest},
                {"scopeCoverageDigest",plan.ScopeCoverageDigest},{"geometryDigest",plan.GeometryDigest} }) receipt[item.Key] = item.Value;
            var rawOwner = prepared.TakeTownSource(token);
            byte[] original = rawOwner.Write(token);
            Need(World13DocumentCodec.Hash(original) == plan.RawDocumentDigest, "Exact original source bytes");
            File.WriteAllBytes(Path.Combine(output,"owned-original-envelope.json"), original);
            var shader = Shader.Find("Universal Render Pipeline/Lit"); Need(shader && shader.isSupported,"Actual URP Lit");
            road = new Material(shader) {name="Derived inspector shared road",color=new Color(.13f,.145f,.16f)};
            terrain = new Material(shader) {name="Derived inspector shared terrain",color=new Color(.28f,.34f,.20f)};
            var physical = Own("Derived Case7 physical owner parent");
            // Factory-owned activation/registration checks remain untouched.
            yield return UploadedGroundOwner800.BuildOwned(physical.transform, plan, road, terrain, token,
                () => sourceCurrent && this && physical && !ending, value => ground = value);
            Need(ground != null && ground.Lease.IsCurrent, "Genuine actual issued ground lease");
            yield return VerifyNativeBuffers(physical);
            town = Own("Derived Case7 actual typed Town").AddComponent<GeoWorld>();
            town.EntryAudit.Enabled = true;
            yield return town.BuildScopedTown800(rawOwner, plan, ground.Lease, token);
            var publication = town.CurrentTownPublication800;
            Need(publication != null && publication.IsCurrent && publication.ValidateSourceNow()
                && ReferenceEquals(publication.Ground,ground.Lease)
                && publication.SourceBuildingCount == 1793, "Genuine same-plan Town/source/Ground");
            Need(World13DocumentCodec.Hash(publication.CopySemanticSourceDocument()) == plan.RawDocumentDigest,
                "Source unchanged after real Town factory");
            File.WriteAllText(Path.Combine(output,"town-ledger.json"),town.ExportScopedTownLedger800());
            File.WriteAllText(Path.Combine(output,"entry-audit.json"),town.EntryAudit.ExportJson());
            var chunks = town.GetComponentsInChildren<BuildingFacadeChunk>(true);
            Need(chunks.Length <= 2500,"Bounded actual Town facade chunks");
            var features = chunks.SelectMany(chunk => chunk.Features).ToArray();
            Need(features.Length <= 100000,"Bounded actual facade feature census");
            var targets = new List<string>();
            for (int index=0;index<TargetIndices.Length;index++)
            {
                Need(town.Region.buildings[TargetIndices[index]].id == TargetSourceIds[index],"Exact actual target source association");
                string prefix = TargetSourceIds[index]+"/";
                var selected = features.Where(feature => feature.Id.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
                Need(selected.Length == (index==0?10:15),"Exact real10+15 target features");
                targets.AddRange(selected.Select(feature => feature.Id));
            }
            Need(targets.Distinct(StringComparer.Ordinal).Count()==25,"Exactly25 distinct actual target feature IDs");
            File.WriteAllText(Path.Combine(output,"actual-target-features.json"),new JArray(features.Where(f=>targets.Contains(f.Id)).Select(f=>
                new JObject {{"id",f.Id},{"door",f.IsDoor},{"position",new JArray(f.Position.x,f.Position.y,f.Position.z)},
                    {"width",f.Width},{"height",f.Height},{"palette",f.Palette}})).ToString(Formatting.Indented));
            ownedMeshes.AddRange(town.GetComponentsInChildren<GeneratedMeshOwner>(true).Select(owner=>owner.Mesh).Where(mesh=>mesh));
            ConfigureCamera(early);
            collector = Case7FacadeCollector800.Create(mode,output,targets);
            collector.Install();
            var affected=town.Region.buildings[TargetIndices[0]];
            Vector3 centre=affected.points.Aggregate(Vector3.zero,(sum,value)=>sum+value)/affected.points.Length;
            Need(plan.TryGround(centre.x,centre.z,out var centreGround),"Target actual final ground");
            centre.y=(float)centreGround.Height+2;
            yield return Capture("affected-house",centre+new Vector3(-14,11,-17),centre);
            Vector3 eye=centre+new Vector3(-14,0,-13);
            Need(plan.TryGround(eye.x,eye.z,out var streetGround),"Actual street camera final ground");
            eye.y=(float)streetGround.Height+1.7f;
            yield return Capture("street",eye,centre);
            receipt["diagnosticAssociationAccepted"] = mode==Case7FacadeCollector800.Mode.Full && !collector.Incomplete;
            receipt["targetFeatures"] = targets.Count;
            receipt["sourceBeforeAfterUnchanged"] = World13DocumentCodec.Hash(publication.CopySemanticSourceDocument())==plan.RawDocumentDigest;
            Need((bool)receipt["sourceBeforeAfterUnchanged"] && errors.Count==0,"Actual source retained and no runtime errors");
            Finish(null);
        }
        IEnumerator VerifyNativeBuffers(GameObject physical)
        {
            var filters=physical.GetComponentsInChildren<MeshFilter>(true).OrderBy(f=>f.name,StringComparer.Ordinal).ToArray();
            Need(filters.Length==ExpectedPackets,"Exact actual native packet count");
            var ids=new HashSet<string>(StringComparer.Ordinal);var rows=new JArray();long vertices=0,triangles=0;
            for(int ordinal=0;ordinal<filters.Length;ordinal++)
            {
                var filter=filters[ordinal];Need(ids.Add(filter.name),"No duplicate actual packet ID");
                var expected=plan.CopyForUpload(filter.name);
                Need(expected.XYZ.Length%3==0&&expected.Indices.Length%3==0&&expected.XYZ.Length<=3*65536&&expected.Indices.Length<=3*131072,
                    "Packet readback budget before bulk native getters");
                var mesh=filter.sharedMesh;var collider=filter.GetComponent<MeshCollider>();
                Need(mesh&&collider&&collider.sharedMesh==mesh&&filter.GetComponent<GeneratedMeshOwner>()?.Mesh==mesh,
                    "Actual MeshFilter/Collider/owner share final buffer");
                Need(mesh.vertexCount==expected.XYZ.Length/3&&mesh.GetIndexCount(0)==expected.Indices.Length,
                    "Actual native counts before arrays");
                var actual=mesh.vertices;var indices=mesh.triangles;
                var xyz=new float[actual.Length*3];
                for(int i=0;i<actual.Length;i++){xyz[3*i]=actual[i].x;xyz[3*i+1]=actual[i].y;xyz[3*i+2]=actual[i].z;}
                string actualHash=BufferHash(xyz,indices),expectedHash=BufferHash(expected.XYZ,expected.Indices);
                Need(actualHash==expectedHash,"Actual native local float bits and indices equal original final plan");
                var matrix=filter.transform.localToWorldMatrix;
                var wanted=Matrix4x4.Translate(new Vector3((float)expected.OriginX,(float)expected.OriginY,(float)expected.OriginZ));
                for(int i=0;i<16;i++)Need(BitConverter.SingleToInt32Bits(matrix[i])==BitConverter.SingleToInt32Bits(wanted[i]),"Exact native packet source frame");
                rows.Add(new JObject{{"id",expected.Id},{"actualBufferSHA256",actualHash},{"expectedBufferSHA256",expectedHash},
                    {"meshId",mesh.GetEntityId().ToString()},{"colliderId",collider.GetEntityId().ToString()},
                    {"matrixFloatBits",new JArray(Enumerable.Range(0,16).Select(i=>BitConverter.SingleToInt32Bits(matrix[i])))},
                    {"vertices",actual.Length},{"triangles",indices.Length/3}});
                ownedMeshes.Add(mesh);vertices+=actual.Length;triangles+=indices.Length/3;
                if(ordinal%32==31)yield return null;
            }
            Need(vertices==ExpectedVertices&&triangles==ExpectedTriangles,"All native buffers accounted; no supplemental physics ray");
            File.WriteAllText(Path.Combine(output,"native-buffer-identities.json"),rows.ToString(Formatting.Indented));
            Need(new FileInfo(Path.Combine(output,"native-buffer-identities.json")).Length<=MaximumJsonBytes,"Bounded identity export");
            receipt["actualPackets"]=filters.Length;receipt["actualVertices"]=vertices;receipt["actualTriangles"]=triangles;
            receipt["actualNativeBufferIdentityVerified"]=true;
        }
        public static string BufferHash(float[] xyz,int[] indices)
        {
            Need(xyz!=null&&indices!=null&&xyz.Length%3==0&&indices.Length%3==0,"Finite complete buffer shape");
            using(var memory=new MemoryStream())
            {
                using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))
                {
                    writer.Write(xyz.Length);writer.Write(indices.Length);
                    foreach(float value in xyz){Need(!float.IsNaN(value)&&!float.IsInfinity(value),"Finite buffer coordinate");writer.Write(BitConverter.SingleToInt32Bits(value));}
                    foreach(int value in indices){Need(value>=0&&value<xyz.Length/3,"In-range native index");writer.Write(value);}
                }
                using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(memory.ToArray())).Replace("-","").ToLowerInvariant();
            }
        }
        void ConfigureCamera(GameObject early)
        {
            early.SetActive(false);
            var sun=Own("Derived inspector daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.1f;
            sun.transform.rotation=Quaternion.Euler(42,-35,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.58f,.60f);
            camera=Own("Derived inspector real URP camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.nearClipPlane=.05f;camera.farClipPlane=4000;camera.fieldOfView=50;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.40f,.52f,.65f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderType=CameraRenderType.Base;
            target=new RenderTexture(Width,Height,24);target.Create();camera.targetTexture=target;camera.aspect=(float)Width/Height;
        }
        IEnumerator Capture(string name,Vector3 position,Vector3 lookAt)
        {
            camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(lookAt-position));
            collector.BeginView(name,camera,target);
            int start=Time.frameCount;double end=Time.realtimeSinceStartupAsDouble+10;
            while(renderedFrame<=start){Need(Time.realtimeSinceStartupAsDouble<end,"Actual completed URP frame");yield return null;}
            Need(ground.Lease.IsCurrent&&town.CurrentTownPublication800.IsCurrent,"Capture retains genuine live context");
            var previous=RenderTexture.active;var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);
            try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
            finally{RenderTexture.active=previous;Destroy(image);}
            bool association=collector.CompleteView(name,camera,target,renderedFrame,out string reason);
            if(mode==Case7FacadeCollector800.Mode.Full)Need(association&&!collector.Incomplete,"Complete actual submission/camera ledger: "+reason);
            views.Add(new JObject{{"name",name},{"frame",renderedFrame},{"position",new JArray(position.x,position.y,position.z)},
                {"target",new JArray(lookAt.x,lookAt.y,lookAt.z)},{"fov",camera.fieldOfView},{"width",Width},{"height",Height},
                {"actualURPRenderTexture",true},{"associationAccepted",association},{"reason",reason},{"labelsDrawn",false},
                {"finalBufferDigest",plan.FinalBufferDigest}});
            File.WriteAllText(Path.Combine(output,"capture-views.json"),views.ToString(Formatting.Indented));
        }
        void Finish(Exception failure)
        {if(ending)return;ending=true;StartCoroutine(GuardCleanup(Cleanup(failure)));}
        IEnumerator GuardCleanup(IEnumerator work)
        {
            Exception failure = null;
            try
            {
                while (true)
                {
                    bool more = false; object next = null;
                    try { more = work.MoveNext(); if (more) next = work.Current; }
                    catch (Exception error) { failure = error; }
                    if (failure != null || !more) yield break;
                    yield return next;
                }
            }
            finally
            {
                try { (work as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
                if (!completionCalled) EmergencyComplete(failure ?? new InvalidOperationException("Cleanup ended without completion"));
            }
        }
        void CompleteOnce(bool passed, Exception failure)
        {
            if (completionCalled) return;
            completionCalled = true;
            var callback = completed; completed = null;
            callback?.Invoke(passed, failure?.ToString());
        }
        void EmergencyComplete(Exception failure)
        {
            // Unexpected cleanup failure must still notify the owning Editor.
            // No claim that this non-yielding fallback verified native destruction.
            void Attempt(Action work) { try { work(); } catch { } }
            Attempt(() => collector?.Dispose());
            Attempt(() => { sourceCurrent = false; cancellation.Cancel(); });
            Attempt(() => ground?.Control.Revoke("Emergency pixel diagnostic cleanup"));
            foreach (var value in owned) Attempt(() => { if (value) Destroy(value); });
            Attempt(() => Time.timeScale = originalScale);
            Attempt(() => RenderPipelineManager.endCameraRendering -= Rendered);
            Attempt(() => Application.logMessageReceived -= Logged);
            Attempt(() => { receipt["status"]="FAIL";receipt["cleanupComplete"]=false;receipt["error"]=failure.ToString();
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString(Formatting.Indented)); });
            CompleteOnce(false, failure);
        }
        IEnumerator Cleanup(Exception failure)
        {
            void Attempt(Action work){try{work();}catch(Exception error){if(failure==null)failure=error;}}
            Attempt(()=>collector?.Dispose());Attempt(()=>{sourceCurrent=false;cancellation.Cancel();});
            double workerDeadline=Time.realtimeSinceStartupAsDouble+5;
            while(preparationWork!=null&&!preparationWork.IsCompleted&&Time.realtimeSinceStartupAsDouble<workerDeadline)yield return null;
            bool workerTerminal=preparationWork==null||preparationWork.IsCompleted;
            receipt["pureWorkerTerminal"]=workerTerminal;
            if(!workerTerminal&&failure==null)failure=new TimeoutException("Pure preparation worker did not terminate after cancellation");
            Attempt(()=>ground?.Control.Revoke("Case7 pixel diagnostic cleanup before teardown"));
            bool retired=false;Attempt(()=>retired=ground==null||!ground.Lease.IsCurrent);
            receipt["actualOwnerRetiredBeforeTeardown"]=retired;
            if(!retired&&failure==null)failure=new InvalidOperationException("Owned issuer failed retirement");
            foreach(var value in owned)if(value)Attempt(()=>Destroy(value));
            yield return null;yield return null;
            bool meshesGone=false;Attempt(()=>meshesGone=ownedMeshes.All(mesh=>!mesh));
            receipt["ownedMeshesReleased"]=meshesGone;
            if(!meshesGone&&failure==null)failure=new InvalidOperationException("Owned native meshes survived cleanup");
            Attempt(()=>{if(road)Destroy(road);});Attempt(()=>{if(terrain)Destroy(terrain);});
            Attempt(()=>{if(target){target.Release();Destroy(target);}});
            yield return null;yield return null;
            bool objectsGone=false,materialsGone=false;
            Attempt(()=>objectsGone=owned.All(value=>!value));
            Attempt(()=>materialsGone=!road&&!terrain&&!target);
            receipt["ownedObjectsReleased"]=objectsGone;receipt["ownedMaterialsAndRTReleased"]=materialsGone;
            receipt["cleanupComplete"]=meshesGone&&objectsGone&&materialsGone&&workerTerminal;
            if((!objectsGone||!materialsGone)&&failure==null)failure=new InvalidOperationException("Owned objects/material/RT survived cleanup");
            Attempt(()=>Time.timeScale=originalScale);
            Attempt(()=>RenderPipelineManager.endCameraRendering-=Rendered);Attempt(()=>Application.logMessageReceived-=Logged);
            receipt["runtimeErrors"]=new JArray(errors);receipt["error"]=failure?.ToString();
            bool passed=failure==null&&errors.Count==0&&retired&&meshesGone&&objectsGone&&materialsGone&&workerTerminal&&views.Count==2;
            receipt["status"]=passed?(mode==Case7FacadeCollector800.Mode.Full?"PASS_FACADE_SUBMISSION_DIAGNOSTIC_PENDING_PIXEL_REVIEW":"PASS_FACADE_CONTROL_ONLY_PENDING_PIXEL_REVIEW"):"FAIL";
            try{File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString(Formatting.Indented));}
            catch(Exception error){passed=false;failure=error;}
            finally{CompleteOnce(passed,failure);}
        }
        void OnDestroy()
        {
            if (!started) return;
            try{collector?.Dispose();}catch{}try{sourceCurrent=false;cancellation.Cancel();}catch{}
            try{ground?.Control.Revoke("Pixel runner destroyed");}catch{}
            foreach (var value in owned) try { if (value) Destroy(value); } catch { }
            try { if (road) Destroy(road); } catch { }
            try { if (terrain) Destroy(terrain); } catch { }
            try { if (target) { target.Release(); Destroy(target); } } catch { }
            try{Time.timeScale=originalScale;}catch{}
            RenderPipelineManager.endCameraRendering-=Rendered;Application.logMessageReceived-=Logged;
        }
    }
}

#endif
