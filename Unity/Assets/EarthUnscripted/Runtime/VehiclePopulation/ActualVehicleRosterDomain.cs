using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EarthUnscripted.VehiclePopulation
{
    /// <summary>
    /// Main-thread membership state for the actual session factories. This type is
    /// internal and issues NO scenery/world authority. Only the session wrapper may
    /// combine its complete roster with live ground/town bindings and actual poses.
    /// </summary>
    internal sealed class ActualVehicleRosterDomain
    {
        // Current production maximum is 28 boot actors, plus race/LAN actors.
        // These refusal caps allow growth without permitting unbounded snapshots.
        internal const int MaximumActors = 128;
        internal const int MaximumPendingFactories = 64;
        internal const int RoadTrafficSlots = 24;
        internal const int RoadPoliceSlots = 3;

        internal enum Role
        {
            Player,
            Traffic,
            Police,
            RaceOpponent,
            LanPeer,
            Service
        }

        /// <summary>Uses managed reference identity, including Unity fake-null wrappers.</summary>
        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object first, object second) => ReferenceEquals(first, second);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }

        internal sealed class Member
        {
            internal readonly string Slot;
            internal readonly string RuntimeHandle = Guid.NewGuid().ToString("N");
            internal readonly Role VehicleRole;
            internal readonly object Root;
            internal int ShapeRevision;
            internal int PoseRevision;
            internal bool Retiring;
            internal bool ShapePending;
            internal bool PosePending;
            internal bool LayoutPending = true; // Registered identity, never complete before actual layout validation.

            internal Member(string slot, Role role, object root)
            {
                Slot = slot;
                VehicleRole = role;
                Root = root;
            }
        }

        /// <summary>
        /// A factory reservation exists BEFORE a root/component is created. Disposing
        /// an unfinished ticket poisons completeness; it never silently closes a gap.
        /// </summary>
        internal sealed class FactoryTicket : IDisposable
        {
            readonly ActualVehicleRosterDomain owner;
            internal readonly string Slot;
            internal readonly Role VehicleRole;
            internal object ObservedRoot;
            internal bool Finished;

            internal FactoryTicket(ActualVehicleRosterDomain owner, string slot, Role role)
            {
                this.owner = owner;
                Slot = slot;
                VehicleRole = role;
            }

            internal void Observe(object actualRoot)
            {
                owner.RequirePending(this);
                if (actualRoot == null ||
                    ObservedRoot != null && !ReferenceEquals(ObservedRoot, actualRoot))
                    throw new InvalidOperationException("One factory ticket must observe one actual vehicle root.");
                ObservedRoot = actualRoot;
            }

            internal Member CommitObservedRoot()
            {
                owner.RequirePending(this);
                if (ObservedRoot == null)
                    throw new InvalidOperationException("No actual component/root was observed by this factory.");
                if (owner.members.Count >= MaximumActors || owner.slots.ContainsKey(Slot) ||
                    owner.members.ContainsKey(ObservedRoot))
                    throw new InvalidOperationException("Duplicate or over-budget actual vehicle registration.");
                var member = new Member(Slot, VehicleRole, ObservedRoot);
                owner.members.Add(ObservedRoot, member);
                owner.orderedMembers=null;
                owner.slots.Add(Slot, member);
                Finished = true;
                owner.pending.Remove(this);
                owner.Revision++;
                return member;
            }

            public void Dispose()
            {
                if (Finished) return;
                Finished = true;
                owner.pending.Remove(this);
                owner.Poison("Factory did not commit its actual root: " + Slot);
            }
        }

        readonly Dictionary<object, Member> members =
            new Dictionary<object, Member>(ReferenceComparer.Instance);
        readonly Dictionary<string, Member> slots =
            new Dictionary<string, Member>(StringComparer.Ordinal);
        readonly HashSet<FactoryTicket> pending = new HashSet<FactoryTicket>();
        readonly HashSet<string> requiredBootSlots;
        bool bootSealed;
        bool revoked;
        string poisonReason;

        internal long Revision { get; private set; }
        internal int MemberCount => members.Count;
        internal bool IsComplete => !revoked && bootSealed && poisonReason == null &&
                                    pending.Count == 0 && members.Values.All(member =>
                                        !member.Retiring && !member.ShapePending && !member.PosePending && !member.LayoutPending);
        internal bool CanSampleActualMembers => !revoked && bootSealed && poisonReason == null &&
            pending.Count == 0 && members.Values.All(member => !member.Retiring);
        internal string UnboundReason => revoked ? "WorldRevoked" : poisonReason ??
            (!bootSealed ? "BootRosterUnsealed" : pending.Count != 0 ? "FactoryPending" :
             members.Values.Any(member => member.Retiring) ? "RetirementPending" :
             members.Values.Any(member => member.ShapePending || member.PosePending) ? "FreshActualSampleRequired" :
             members.Values.Any(member => member.LayoutPending) ? "PhysicalLayoutValidationPending" : "");

        /// <param name="actualCircuitWorld">
        /// Supplied ONLY by the session's private wrapper from its actual World.IsCircuit,
        /// never from a public readiness flag or a caller-provided expected actor count.
        /// </param>
        internal ActualVehicleRosterDomain(bool actualCircuitWorld)
        {
            requiredBootSlots = new HashSet<string>(StringComparer.Ordinal) { "player" };
            if (actualCircuitWorld) return;
            for (int index = 0; index < RoadTrafficSlots; index++)
                requiredBootSlots.Add("traffic/" + index);
            for (int index = 0; index < RoadPoliceSlots; index++)
                requiredBootSlots.Add("police/" + index);
        }

        internal FactoryTicket BeginFactory(string slot, Role role)
        {
            if (revoked || string.IsNullOrEmpty(slot) || slot.Length > 128 ||
                pending.Count >= MaximumPendingFactories || pending.Any(ticket => ticket.Slot == slot))
                throw new InvalidOperationException("Invalid, duplicate or over-budget factory reservation.");
            var result = new FactoryTicket(this, slot, role);
            pending.Add(result);
            Revision++;
            return result;
        }

        void RequirePending(FactoryTicket ticket)
        {
            if (revoked || ticket.Finished || !pending.Contains(ticket))
                throw new InvalidOperationException("Expired factory ticket.");
        }

        // The authoritative source slots stay required while supported traffic
        // factories are deferred. Incomplete is unavailable, never a smaller
        // supposedly complete scene. Undeclared actors remain an error.
        internal bool TrySealDeferredActualBootRoster()
        {
            if (bootSealed) return true;
            if (revoked || poisonReason != null || !requiredBootSlots.IsSupersetOf(slots.Keys))
                throw new InvalidOperationException("Actual source boot roster was revoked, poisoned or has undeclared actors.");
            if (pending.Count != 0 || !requiredBootSlots.SetEquals(slots.Keys) ||
                members.Values.Any(member => member.LayoutPending)) return false;
            SealActualBootRoster();
            return true;
        }

        internal void SealActualBootRoster()
        {
            if (revoked || pending.Count != 0 || poisonReason != null || members.Values.Any(member => member.LayoutPending) ||
                !requiredBootSlots.SetEquals(slots.Keys) || slots.Any(pair =>
                    pair.Value.VehicleRole != (pair.Key == "player" ? Role.Player :
                        pair.Key.StartsWith("traffic/", StringComparison.Ordinal) ? Role.Traffic : Role.Police)))
                throw new InvalidOperationException("Actual source boot roster is incomplete or contains undeclared actors.");
            bootSealed = true;
            Revision++;
        }

        internal Member Find(object root) => root != null && members.TryGetValue(root, out var member)
            ? member : null;

        IReadOnlyList<Member> orderedMembers;
        // Membership order changes only at actual factory commit/retirement.
        // Expose a read-only view to the internal phase sampler; CopyMembers
        // still returns a detached array for existing snapshot consumers.
        internal IReadOnlyList<Member> OrderedMembers => orderedMembers ??
            (orderedMembers=Array.AsReadOnly(members.Values.OrderBy(member=>member.Slot,StringComparer.Ordinal).ToArray()));
        internal Member[] CopyMembers() => OrderedMembers.ToArray();

        internal void BeginRetirement(object root)
        {
            var member = Find(root) ?? throw new InvalidOperationException("Retirement of unregistered vehicle.");
            member.Retiring = true;
            Revision++;
        }

        /// <summary>Called only after the wrapper confirms actual inactive/destroyed physics.</summary>
        internal void ConfirmActualRetirement(object root)
        {
            var member = Find(root);
            if (member == null) return; // Repeated OnDisable/OnDestroy confirmation is harmless.
            if (!member.Retiring)
            {
                Poison("Unexpected vehicle removal: " + member.Slot);
                return;
            }
            members.Remove(root);
            orderedMembers=null;
            slots.Remove(member.Slot);
            Revision++;
        }

        internal void InvalidateShape(object root)
        {
            var member = Find(root) ?? throw new InvalidOperationException("Unknown vehicle shape mutation.");
            member.ShapeRevision++;
            member.ShapePending = true;
            member.LayoutPending = true;
            Revision++;
        }

        /// <summary>Only the Unity wrapper calls this after exact actual collider validation.</summary>
        internal void ConfirmPhysicalLayout(object root)
        {
            var member = Find(root) ?? throw new InvalidOperationException("Unknown validated physical root.");
            member.LayoutPending = false;
            Revision++;
        }

        internal void InvalidatePose(object root)
        {
            var member = Find(root);
            if (member == null) return; // Initial construction is already covered by its pending ticket.
            member.PoseRevision++;
            member.PosePending = true;
            Revision++;
        }

        /// <summary>
        /// Called by the private Unity sampler after checking the actual body,
        /// model and complete collider subtree. Never exposed as a caller bool.
        /// </summary>
        internal void ConfirmFreshActualSample(object root)
        {
            var member = Find(root) ?? throw new InvalidOperationException("Unknown sampled vehicle.");
            member.ShapePending = false;
            member.PosePending = false;
        }

        internal void ObserveUnownedCreation() => Poison("Unreserved vehicle component creation.");

        internal void Poison(string reason)
        {
            if (poisonReason == null) poisonReason = reason;
            Revision++;
        }

        internal void Revoke()
        {
            revoked = true;
            bootSealed = false;
            Revision++;
        }
    }
}
