using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadRush.VehiclePopulation
{
    /// <summary>
    /// Exact physical box references recorded by VehicleGeometry's real factories.
    /// Decorative primitives temporarily retain disabled, deferred-Destroy native
    /// colliders; they are NOT recorded here and never become accepted physics.
    /// A matching model has one active chassis, or chassis+four active race wheels.
    /// Genuine retained race-wheel boxes may remain inactive on a stock model.
    /// Main-thread, session-root local metres; no caller readiness/count flag.
    /// </summary>
    internal sealed class ActualVehiclePhysicalLayout : MonoBehaviour
    {
        const int WheelCount = 4;
        BoxCollider chassis;
        readonly BoxCollider[] wheels = new BoxCollider[WheelCount];
        readonly List<Collider> supplements = new List<Collider>();
        internal static void RecordSupplemental(Transform root, Collider collider)
        {
            var layout=root.GetComponent<ActualVehiclePhysicalLayout>();
            if(!layout || !collider || collider.transform==root || !collider.transform.IsChildOf(root))
                throw new InvalidOperationException("Supplemental collider must be a real factory child.");
            if(!layout.supplements.Contains(collider))layout.supplements.Add(collider);
        }
        internal int ActiveSupplementCount => supplements.Count(c=>c&&c.transform.IsChildOf(transform)&&c.enabled&&c.gameObject.activeInHierarchy);
        int model = -1;
        readonly AllocatedWheelReferenceBook provenance = new AllocatedWheelReferenceBook();

        internal static void RecordChassis(BoxCollider actualBox, int actualModel)
        {
            if (!actualBox) return;
            var layout = actualBox.GetComponent<ActualVehiclePhysicalLayout>();
            if (!layout) layout = actualBox.gameObject.AddComponent<ActualVehiclePhysicalLayout>();
            layout.chassis = actualBox;
            layout.model = actualModel;
            layout.supplements.RemoveAll(c=>!c||!c.transform.IsChildOf(actualBox.transform));
            // Rebuild may have detached old disabled visual/physical children.
            // Retain only actual factory wheel boxes still belonging to this root.
            for (int index = 0; index < WheelCount; index++)
                if (!ReferenceEquals(layout.wheels[index], null) &&
                    (!layout.wheels[index] || !layout.wheels[index].transform.IsChildOf(actualBox.transform)))
                {
                    layout.provenance.ForgetDetached(index, layout.wheels[index]);
                    layout.wheels[index] = null;
                }
        }

        internal static void RecordAllocatedWheel(Transform actualRoot, int index, BoxCollider actualBox)
        {
            var layout = actualRoot.GetComponent<ActualVehiclePhysicalLayout>();
            if (!layout || index < 0 || index >= WheelCount) return;
            layout.provenance.RecordNewAllocation(index, actualBox);
            layout.wheels[index] = actualBox;
        }

        internal static void VerifyRetainedWheel(Transform actualRoot, int index, BoxCollider actualBox)
        {
            var layout = actualRoot.GetComponent<ActualVehiclePhysicalLayout>();
            if (!layout) return; // Missing chassis provenance is rejected by ReadBoxes.
            layout.provenance.VerifyRetained(index, actualBox);
        }

        internal Collider[] ReadConstructingBoxes(Rigidbody body, int actualModel) => ReadBoxes(body, actualModel, false);
        internal Collider[] ReadValidatedBoxes(Rigidbody body, int actualModel) => ReadBoxes(body, actualModel, true);

        // A constructing identity may await native attachment; its real hierarchy
        // must already belong to the exact body. Full validation requires BOTH.
        Collider[] ReadBoxes(Rigidbody body, int actualModel, bool requireNativeAttachment)
        {
            if (!provenance.Healthy)
                throw new InvalidOperationException("Existing wheel is not the previously allocated physical reference.");
            if (!body || body.gameObject != gameObject || !chassis || chassis.transform != transform ||
                model != actualModel)
                throw new InvalidOperationException("Actual factory chassis/model/body provenance unavailable.");
            var result = new List<Collider> { chassis };
            for (int index = 0; index < WheelCount; index++)
            {
                var wheel = wheels[index];
                if (wheel && wheel.transform.IsChildOf(transform)) result.Add(wheel);
                else if (actualModel == 3)
                    throw new InvalidOperationException("Actual single-seat factory wheel provenance missing.");
            }
            foreach(var supplement in supplements)if(supplement&&supplement.transform.IsChildOf(transform))result.Add(supplement);
            if (result.Distinct().Count() != result.Count || result.Any(box =>
                box.GetComponentInParent<Rigidbody>(true) != body || box.isTrigger ||
                (requireNativeAttachment && box.enabled && box.gameObject.activeInHierarchy ?
                    box.attachedRigidbody != body : box.attachedRigidbody && box.attachedRigidbody != body)))
                throw new InvalidOperationException("Actual physical box identity/body/solid binding differs.");
            foreach (var box in result)
            {
                bool active = box.enabled && box.gameObject.activeInHierarchy;
                bool requiredActive = box == chassis || supplements.Contains(box) || actualModel == 3;
                if (active != requiredActive)
                    throw new InvalidOperationException("Actual factory physical activation layout differs.");
            }
            return result.ToArray();
        }
    }
}
