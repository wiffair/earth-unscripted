using System;
using UnityEngine;
using EarthUnscripted.VehiclePopulation;
using EarthUnscripted.FunnyAnimals.VehicleCaptureProposal;

namespace EarthUnscripted.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>
    /// Detached actual actor identity/revision plus PHYSICAL source-box envelope.
    /// It is not currentness, semantic Town authority, visual coverage or future safety.
    /// RuntimeHandle is opaque incarnation identity, never a provider/network identifier.
    /// </summary>
    public readonly struct RegisteredPhysicalEnvelope
    {
        public readonly string RuntimeHandle;
        public readonly int Model;
        public readonly int ShapeRevision;
        public readonly int PoseDiscontinuityRevision;
        public readonly PhysicalEnvelopeWitness Physical;
        public bool WholeVehicleClearanceBound => false;

        internal RegisteredPhysicalEnvelope(ActualVehicleSample source, PhysicalEnvelopeWitness physical)
        {
            RuntimeHandle = source.RuntimeHandle;
            Model = source.Model;
            ShapeRevision = source.ShapeRevision;
            PoseDiscontinuityRevision = source.PoseDiscontinuityRevision;
            Physical = physical;
        }
    }

    /// <summary>
    /// Converts ACTUAL frozen Registry1 detached fields, without native handles or
    /// registry construction. Caller must acquire its own actual population snapshot
    /// and validate its actual Town/ground coherence/currentness at immediate commit.
    /// No fake sample constructor, readiness callback, source header or empty roster.
    /// This method never calls IsCurrent per actor: that bounded but allocating owner
    /// read belongs once at batch acquisition/commit, not hidden inside every box.
    /// </summary>
    internal static class ActualSamplePhysicalConverter
    {
        private const int MaximumPublishedParts = 8; // exact Registry1 publication fence

        internal static bool TryConvert(ActualVehicleSample source,
            out RegisteredPhysicalEnvelope envelope, out string reason)
        {
            envelope = default;
            reason = "Actual immutable vehicle sample unavailable";
            // Cheap detached-data fences precede all processing. This converter performs
            // no engine access; it cannot authenticate which live registry owns source.
            if (source == null || string.IsNullOrEmpty(source.RuntimeHandle)
                || source.ShapeRevision < 0 || source.PoseDiscontinuityRevision < 0
                || source.Boxes == null || source.Boxes.Count == 0 || source.Boxes.Count > MaximumPublishedParts)
                return false;
            if (!TryAffine(source.VisibleRootToWorld, out Affine3 renderRoot))
            {
                reason = "Actual captured render-root matrix is not finite affine";
                return false;
            }
            var boxes = new BoxWitness[source.Boxes.Count];
            for (int index = 0; index < boxes.Length; index++)
            {
                VehicleBoxSample box = source.Boxes[index];
                if (!TryAffine(box.BoxToRoot, out Affine3 boxFrame))
                {
                    reason = "Actual captured box matrix is not finite affine";
                    return false;
                }
                boxes[index] = new BoxWitness(Point(box.CentreMetres),
                    new Point3((double)box.SizeMetres.x * 0.5, (double)box.SizeMetres.y * 0.5,
                        (double)box.SizeMetres.z * 0.5), boxFrame, box.ActiveForPhysics);
            }
            if (!CompoundBoxEnvelope.TryCompute(source.Model, boxes, renderRoot, Point(source.BodyPositionMetres),
                out PhysicalEnvelopeWitness physical, out reason)) return false;
            envelope = new RegisteredPhysicalEnvelope(source, physical);
            return true;
        }

        private static Point3 Point(Vector3 source) => new Point3(source.x, source.y, source.z);

        // Read actual float struct fields by value, preserving each original value.
        // Reject projective matrices instead of silently dropping their fourth row.
        private static bool TryAffine(Matrix4x4 source, out Affine3 affine)
        {
            affine = new Affine3(new Point3(source.m00, source.m10, source.m20),
                new Point3(source.m01, source.m11, source.m21),
                new Point3(source.m02, source.m12, source.m22),
                new Point3(source.m03, source.m13, source.m23));
            return affine.Finite && source.m30 == 0 && source.m31 == 0
                && source.m32 == 0 && source.m33 == 1;
        }
    }
}
