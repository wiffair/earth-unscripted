using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RoadRush.Core800
{
    // Physical owner of one complete, already verified DEVELOPMENT800 plan.
    // No source/provider/registry/world13 authority and no default boot hook.
    [DefaultExecutionOrder(-10000)]
    public sealed class UploadedGroundOwner800 : MonoBehaviour
    {
        // The host stays active during preparation so OnDestroy can retire a
        // never-active physical child; see the retained inactive-wrapper failure.
        private const int PhysicalRootCount = 16;
        private const int MaximumSubscribers = 128; // Subscribe systems, not every scenery instance.
        private const int MaximumCallbackErrors = 16; // Bounded diagnostics; every callback is still attempted.
        private const int MaximumErrorCharacters = 16384;
        enum State
        {
            Unissued,
            Preparing,
            Published,
            Revoking,
            Revoked
        }

        sealed class RootBinding
        {
            internal Transform Transform;
            internal Matrix4x4 Frame;
            internal int Children;
        }

        sealed class PacketBinding
        {
            internal string Id;
            internal Transform Transform;
            internal Matrix4x4 Frame;
            internal MeshFilter Filter;
            internal MeshCollider Collider;
            internal MeshRenderer Renderer;
            internal GeneratedMeshOwner Owner;
            internal Mesh Mesh;
            internal Material Material;
            internal int Layer;
        }

        sealed class Subscription : IDisposable
        {
            internal UploadedGroundOwner800 Owner;
            internal int ThreadId;
            internal Action<GroundLease800, string> Callback;
            public void Dispose()
            {
                if (Thread.CurrentThread.ManagedThreadId != ThreadId)
                    throw new InvalidOperationException("Ground subscription disposal requires main thread");
                var owner = Owner;
                Owner = null;
                Callback = null;
                if (!ReferenceEquals(owner, null))
                    owner.subscribers.Remove(this);
            }
        }

        readonly object issueKey = new object ();
        // Original allocated meshes only. Borrowed material/primitive substitutions are never owned.
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<GeneratedMeshOwner> meshOwners = new List<GeneratedMeshOwner>();
        readonly List<Subscription> subscribers = new List<Subscription>();
        readonly List<string> callbackErrors = new List<string>();
        State state;
        // Managed main-thread ID and transient owner generation, never provider identity.
        int thread, generation;
        // At most one full packet/reference/frame scan per rendered frame.
        int validatedFrame = int.MinValue;
        Transform expectedParent, expectedHostParent;
        GameObject ownedRoot, host;
        Matrix4x4 rootFrame, hostFrame;
        CancellationToken stageToken;
        Func<bool> stageCurrent;
        CancellationTokenSource stageCancellation;
        RootBinding[] roots = Array.Empty<RootBinding>();
        PacketBinding[] packets = Array.Empty<PacketBinding>();
        Dictionary<Collider, string> colliderIds = new Dictionary<Collider, string>();
        IEnumerator pendingUpload;
        GroundLease800 lease;
        GroundBankFootprintIndex800 bankIndex;
        internal PreparedWorldMeshPlan800 Plan { get; private set; }
        internal string RuntimeHandle { get; private set; }
        public int FullValidationPasses { get; private set; }
        public int Generation => generation;

        public string[] CopyRevocationCallbackErrors()
        {
            MainThread();
            return callbackErrors.ToArray();
        }

        static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
                if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
                    return false;
            return true;
        }

        static bool Identity(Matrix4x4 m)
        {
            var id = Matrix4x4.identity;
            for (int i = 0; i < 16; i++)
                if (m[i] != id[i])
                    return false;
            return true;
        }

        static void Need(bool okay, string why)
        {
            if (!okay)
                throw new InvalidDataException("Uploaded800: " + why);
        }

        void MainThread()
        {
            if (thread == 0 || Thread.CurrentThread.ManagedThreadId != thread)
                throw new InvalidOperationException("Uploaded ground lease/cleanup requires its main thread");
        }

        void CaptureGroup(GameObject group)
        {
            foreach (var filter in group.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                Need(mesh && filter.GetComponent<GeneratedMeshOwner>()?.Mesh == mesh, "Actual generated mesh before handoff");
                meshes.Add(mesh);
                meshOwners.Add(filter.GetComponent<GeneratedMeshOwner>());
            }
        }

        /// <summary>Compatibility consumer-only path. Scene owners should use BuildOwned so retirement has an explicit control handle.</summary>
        public static IEnumerator Build(Transform identityParent, PreparedWorldMeshPlan800 plan, Material sharedRoad, Material sharedTerrain, CancellationToken token, Func<bool> requestCurrent, Action<GroundLease800> completed)
        {
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));
            var build = BuildOwned(identityParent, plan, sharedRoad, sharedTerrain, token, requestCurrent, result => completed(result.Lease));
            try
            {
                while (build.MoveNext())
                    yield return build.Current;
            }
            finally
            {
                (build as IDisposable)?.Dispose();
            }
        }

        /// <summary>Stages exact geometry, syncs physical transforms, then returns caller cleanup control separately from the consumer's readonly lease.</summary>
        public static IEnumerator BuildOwned(Transform identityParent, PreparedWorldMeshPlan800 plan, Material sharedRoad, Material sharedTerrain, CancellationToken token, Func<bool> requestCurrent, Action<UploadedGroundBuild800> completed)
        {
            if (identityParent && (!identityParent.gameObject.activeInHierarchy || !Identity(identityParent.localToWorldMatrix)))
                throw new InvalidOperationException("Identity source frame parent required");
            Need(plan != null && plan.SourceSubsetOnly && !plan.FullWorldAdmitted && sharedRoad && sharedTerrain && requestCurrent != null && completed != null, "Typed scoped plan, borrowed shared materials, source generation predicate and receiver required");
            token.ThrowIfCancellationRequested();
            if (!requestCurrent())
                throw new OperationCanceledException("Uploaded800 source request stale");
            var host = new GameObject("Uploaded800 lifetime owner");
            host.transform.SetParent(identityParent, false);
            var owner = host.AddComponent<UploadedGroundOwner800>();
            owner.thread = Thread.CurrentThread.ManagedThreadId;
            owner.host = host;
            owner.expectedHostParent = identityParent;
            owner.hostFrame = host.transform.localToWorldMatrix;
            var root = new GameObject("Uploaded800 PHYSICAL ONLY");
            root.SetActive(false);
            root.transform.SetParent(host.transform, false);
            owner.ownedRoot = root;
            owner.expectedParent = host.transform;
            owner.Plan = plan;
            owner.RuntimeHandle = Guid.NewGuid().ToString("N");
            owner.generation = 1;
            owner.state = State.Preparing;
            bool handed = false;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            owner.stageCancellation = cancellation;
            owner.stageToken = token;
            owner.stageCurrent = requestCurrent;
            var footprint = Task.Run(() => new GroundBankFootprintIndex800(plan, cancellation.Token), cancellation.Token);
            void Check()
            {
                token.ThrowIfCancellationRequested();
                if (!owner || owner.state != State.Preparing || !root || root.activeInHierarchy || !requestCurrent())
                    throw new OperationCanceledException("Uploaded800 build superseded/destroyed");
            }

            try
            {
                while (!footprint.IsCompleted)
                {
                    Check();
                    yield return null;
                }

                owner.bankIndex = footprint.GetAwaiter().GetResult();
                Check();
                var keys = plan.CopyRootKeys();
                Need(keys.Length == PhysicalRootCount, "All16physical roots");
                foreach (var key in keys)
                {
                    // A destroyed host may have disposed the prior suspended uploader.
                    // Recheck before any Unity getter on the next root, not only after MoveNext.
                    Check();
                    var upload = WorldMeshRootUpload800.BuildHidden(root.transform, plan, key, role => role == PreparedWorldMeshPlan800.Role.GroundRoad ? sharedRoad : sharedTerrain, cancellation.Token, () => owner && owner.state == State.Preparing && requestCurrent(), owner.CaptureGroup).GetEnumerator();
                    owner.pendingUpload = upload;
                    try
                    {
                        while (upload.MoveNext())
                        {
                            Check();
                            yield return upload.Current;
                        }
                    }
                    finally
                    {
                        upload.Dispose();
                        if (ReferenceEquals(owner.pendingUpload, upload))
                            owner.pendingUpload = null;
                    }
                }

                Check();
                owner.ValidateReadiness(sharedRoad, sharedTerrain);
                root.SetActive(true);
                // Ready means raw raycasts can see these colliders NOW, including
                // projects with autoSyncTransforms=false; no simulation/timestep change.
                Physics.SyncTransforms();
                owner.state = State.Published;
                owner.validatedFrame = int.MinValue;
                Need(owner.ValidateLayout(), "Activated exact physical layout");
                owner.lease = new GroundLease800(owner, owner.issueKey, owner.generation, owner.rootFrame);
                completed(new UploadedGroundBuild800(owner, owner.issueKey, owner.lease));
                owner.stageCurrent = null;
                owner.stageCancellation = null;
                handed = true;
            }
            finally
            {
                cancellation.Cancel();
                if (!footprint.IsCompleted)
                    footprint.ContinueWith(t =>
                    {
                        var observed = t.Exception;
                    }, TaskContinuationOptions.OnlyOnFaulted);
                // Managed owner remains a cleanup target even if Unity's wrapper is fake-null.
                if (!handed)
                    owner.ReleaseOwned("UnpublishedBuildCancelled", true);
            }
        }

        void ValidateReadiness(Material road, Material terrain)
        {
            MainThread();
            Need(state == State.Preparing && ownedRoot && !ownedRoot.activeInHierarchy && ownedRoot.transform.childCount == PhysicalRootCount, "Inactive complete hierarchy before readiness");
            var all = ownedRoot.GetComponentsInChildren<MeshFilter>(true);
            Need(all.Length == Plan.PacketCount && meshes.Count == all.Length && meshes.Distinct().Count() == all.Length && ownedRoot.GetComponentsInChildren<Collider>(true).Length == all.Length, "Physical subtree only; no overlay/unowned collider");
            var wanted = Plan.CopyRootKeys().SelectMany(Plan.CopyRootPacketIds).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Need(all.Select(f => f.name).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(wanted), "Exact packet IDs once");
            rootFrame = ownedRoot.transform.localToWorldMatrix;
            Need(Identity(rootFrame) && ReferenceEquals(ownedRoot.transform.parent, expectedParent), "Source frame remains identity");
            roots = Enumerable.Range(0, PhysicalRootCount).Select(i => ownedRoot.transform.GetChild(i)).Select(t => new RootBinding { Transform = t, Frame = t.localToWorldMatrix, Children = t.childCount }).ToArray();
            var rows = new List<PacketBinding>();
            long vertices = 0, triangles = 0;
            foreach (var f in all.OrderBy(f => f.name, StringComparer.Ordinal))
            {
                var data = Plan.CopyForUpload(f.name);
                var mc = f.GetComponent<MeshCollider>();
                var renderer = f.GetComponent<MeshRenderer>();
                var generated = f.GetComponent<GeneratedMeshOwner>();
                Material expected = Plan.PacketRole(data.Id) == PreparedWorldMeshPlan800.Role.GroundRoad ? road : terrain;
                Need(expected && mc && mc.enabled && !mc.convex && !mc.isTrigger && renderer && renderer.enabled && generated && generated.Mesh == f.sharedMesh && mc.sharedMesh == f.sharedMesh && renderer.sharedMaterial == expected && f.gameObject.activeSelf, "Actual MF/MC/generated/shared material equality");
                Need(f.transform.childCount == 0 && Identity(f.transform.parent.localToWorldMatrix), "Original packet transform parent; no overlay children");
                var position = f.transform.localPosition;
                Need(BitConverter.SingleToInt32Bits(position.x) == BitConverter.SingleToInt32Bits((float)data.OriginX) && BitConverter.SingleToInt32Bits(position.y) == BitConverter.SingleToInt32Bits((float)data.OriginY) && BitConverter.SingleToInt32Bits(position.z) == BitConverter.SingleToInt32Bits((float)data.OriginZ) && f.transform.localRotation.x == 0 && f.transform.localRotation.y == 0 && f.transform.localRotation.z == 0 && f.transform.localRotation.w == 1 && f.transform.localScale.x == 1 && f.transform.localScale.y == 1 && f.transform.localScale.z == 1, "Exact original per-packet origins, no repacking");
                var actual = f.sharedMesh.vertices;
                Need(actual.Length * 3 == data.XYZ.Length && f.sharedMesh.triangles.SequenceEqual(data.Indices), "Actual final original buffer lengths/indices");
                for (int i = 0; i < actual.Length; i++)
                    Need(BitConverter.SingleToInt32Bits(actual[i].x) == BitConverter.SingleToInt32Bits(data.XYZ[i * 3]) && BitConverter.SingleToInt32Bits(actual[i].y) == BitConverter.SingleToInt32Bits(data.XYZ[i * 3 + 1]) && BitConverter.SingleToInt32Bits(actual[i].z) == BitConverter.SingleToInt32Bits(data.XYZ[i * 3 + 2]), "Actual exact final vertex bits");
                vertices += actual.Length;
                triangles += data.Indices.Length / 3;
                var b = new PacketBinding
                {
                    Id = data.Id,
                    Transform = f.transform,
                    Frame = f.transform.localToWorldMatrix,
                    Filter = f,
                    Collider = mc,
                    Renderer = renderer,
                    Owner = generated,
                    Mesh = f.sharedMesh,
                    Material = expected,
                    Layer = f.gameObject.layer
                };
                rows.Add(b);
                colliderIds.Add(mc, b.Id);
            }

            Need(vertices == Plan.VertexCount && triangles == Plan.TriangleCount, "Exact aggregate final physical buffers");
            packets = rows.ToArray();
        }

        bool BasicLayout()
        {
            if (!host || !host.activeInHierarchy || !ReferenceEquals(host.transform.parent, expectedHostParent) || !Same(hostFrame, host.transform.localToWorldMatrix) || !ownedRoot || !ownedRoot.activeInHierarchy || !ReferenceEquals(ownedRoot.transform.parent, expectedParent) || ownedRoot.transform.childCount != PhysicalRootCount || !Same(rootFrame, ownedRoot.transform.localToWorldMatrix))
                return false;
            foreach (var r in roots)
                if (!r.Transform || !ReferenceEquals(r.Transform.parent, ownedRoot.transform) || r.Transform.childCount != r.Children || !r.Transform.gameObject.activeInHierarchy || !Same(r.Frame, r.Transform.localToWorldMatrix))
                    return false;
            return true;
        }

        bool ValidateLayout()
        {
            MainThread();
            FullValidationPasses++;
            if (!BasicLayout())
                return false;
            foreach (var p in packets)
                if (!p.Transform || !p.Filter || !p.Collider || !p.Renderer || !p.Owner || !p.Mesh || !p.Transform.gameObject.activeInHierarchy || p.Transform.childCount != 0 || !p.Collider.enabled || p.Collider.isTrigger || p.Collider.convex || !p.Renderer.enabled || !p.Material || p.Collider.sharedMesh != p.Mesh || p.Filter.sharedMesh != p.Mesh || p.Owner.Mesh != p.Mesh || p.Renderer.sharedMaterial != p.Material || p.Transform.gameObject.layer != p.Layer || !Same(p.Frame, p.Transform.localToWorldMatrix))
                    return false;
            validatedFrame = Time.frameCount;
            return true;
        }

        internal void AuthorizeLeaseIssuance(object key, int g)
        {
            MainThread();
            Need(ReferenceEquals(key, issueKey) && state == State.Published && generation == g && lease == null && validatedFrame == Time.frameCount, "Actual private readiness only");
        }

        internal void AuthorizeCallerControl(object key, GroundLease800 candidate)
        {
            MainThread();
            Need(ReferenceEquals(key, issueKey) && ReferenceEquals(candidate, lease) && state == State.Published && CheckLeaseCurrent(candidate.Generation), "Caller control requires the actual issued lease");
        }

        internal bool CheckLeaseCurrent(int expected)
        {
            MainThread();
            if (state != State.Published || generation != expected)
                return false;
            if (!BasicLayout() || validatedFrame != Time.frameCount && !ValidateLayout())
            {
                ReleaseOwned("PhysicalLayoutChanged", true);
                return false;
            }

            return true;
        }

        internal string[] CopyPhysicalPacketIds() => packets.Select(p => p.Id).ToArray();
        internal bool FootprintSupported(Vector2[] footprint, double margin, out string reason, CancellationToken token) => bankIndex.SupportedOutsideBanks(footprint, margin, out reason, token);
        internal IDisposable RegisterBeforeRevoke(GroundLease800 requested, Action<GroundLease800, string> callback)
        {
            MainThread();
            if (!ReferenceEquals(lease, requested) || !CheckLeaseCurrent(requested.Generation) || callback == null || subscribers.Count >= MaximumSubscribers)
                throw new InvalidOperationException("Live issued lease and bounded main-thread callback required");
            var s = new Subscription
            {
                Owner = this,
                ThreadId = thread,
                Callback = callback
            };
            subscribers.Add(s);
            return s;
        }

        public bool ValidatePhysicalLayoutNow()
        {
            MainThread();
            validatedFrame = int.MinValue;
            return CheckLeaseCurrent(generation);
        }

        public void Revoke(string reason)
        {
            MainThread();
            if (state == State.Unissued)
                throw new InvalidOperationException("Unissued component owns no ground");
            ReleaseOwned(string.IsNullOrEmpty(reason) ? "ExplicitRevocation" : reason, true);
        }

        void Update()
        {
            if (state == State.Published)
                CheckLeaseCurrent(generation);
            else if (state == State.Preparing && (stageToken.IsCancellationRequested || stageCurrent == null || !stageCurrent() || !ownedRoot))
                ReleaseOwned("PreparationSourceLifetimeEnded", true);
        }

        void OnDisable()
        {
            if (state == State.Published || state == State.Preparing)
                ReleaseOwned("OwnerDisabled", true);
        }

        void OnDestroy()
        {
            if (state != State.Unissued && state != State.Revoked)
                ReleaseOwned("OwnerDestroyed", false);
        }

        void ReleaseOwned(string reason, bool destroyRoot)
        {
            MainThread();
            if (state == State.Revoked || state == State.Revoking)
                return;
            state = State.Revoking;
            generation++;
            validatedFrame = int.MinValue;
            stageCancellation?.Cancel();
            stageCancellation = null;
            stageCurrent = null;
            var callbacks = subscribers.Select(s => (Subscription: s, Callback: s.Callback)).ToArray();
            subscribers.Clear();
            foreach (var entry in callbacks)
            {
                entry.Subscription.Owner = null;
                entry.Subscription.Callback = null;
                try
                {
                    entry.Callback(lease, reason);
                }
                catch (Exception e)
                {
                    if (callbackErrors.Count < MaximumCallbackErrors)
                    {
                        var text = e.ToString();
                        callbackErrors.Add(text.Length <= MaximumErrorCharacters ? text : text.Substring(0, MaximumErrorCharacters));
                    }
                }
            }

            try
            {
                var pending = pendingUpload;
                pendingUpload = null;
                (pending as IDisposable)?.Dispose();
            }
            finally
            {
                // Disarm recorded wrappers first. Only ORIGINAL generated Mesh references
                // belong to us, never borrowed primitives/materials/renderer substitutions.
                foreach (var o in meshOwners)
                    if (!ReferenceEquals(o, null))
                        o.Mesh = null;
                if (ownedRoot)
                    ownedRoot.SetActive(false);
                foreach (var m in meshes)
                    if (m)
                    {
                        if (Application.isPlaying)
                            Destroy(m);
                        else
                            DestroyImmediate(m);
                    }

                meshes.Clear();
                meshOwners.Clear();
                colliderIds.Clear();
                state = State.Revoked;
                if (destroyRoot && host)
                {
                    if (Application.isPlaying)
                        Destroy(host);
                    else
                        DestroyImmediate(host);
                }
            }
        }
    }
}
