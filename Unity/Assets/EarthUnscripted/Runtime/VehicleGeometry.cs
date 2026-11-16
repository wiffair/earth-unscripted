using System;using UnityEngine;using EarthUnscripted.Core;
namespace EarthUnscripted
{
    // Shared factory geometry; role-specific vertical physics remains unchanged.
    public static class VehicleGeometry
    {
        const int PhysicalWheelCount = 4;
        const int SingleSeatModel = 3;
        const float RaceBodyCentreYMetres = .42f;
        const float RaceBodyHeightMetres = .57f;
        const float VerticalFitToleranceMetres = .0001f;
        const float RaceWheelBoxHeightMetres = .5f;
        const float RaceWheelBoxLengthMetres = .5f;

        /// <summary>Preserve role vertical fit; apply metric XZ, then record this actual chassis reference.</summary>
        public static void ApplyCollider(BoxCollider box, int model)
        {
            if (!box) throw new ArgumentNullException(nameof(box));
            var metric = VehicleDimensions.ForModel(model);
            if (box.center.y <= 0 || box.size.y <= 0)
                throw new InvalidOperationException("Initialize role vertical collider before metric XZ fit");
            var centre = box.center;
            var size = box.size;
            centre.x = 0; centre.z = metric.ColliderCentreZ;
            size.x = metric.ColliderWidth; size.z = metric.ColliderLength;
            if (model == SingleSeatModel)
            { centre.y = RaceBodyCentreYMetres; size.y = RaceBodyHeightMetres; }
            else if (Mathf.Abs(centre.y - RaceBodyCentreYMetres) < VerticalFitToleranceMetres &&
                Mathf.Abs(size.y - RaceBodyHeightMetres) < VerticalFitToleranceMetres)
            { centre.y = VehicleMetric.ColliderCentreY; size.y = VehicleMetric.ColliderHeight; }
            if (model >= ImportedVehicleCatalogue.BuiltInCount)
            { centre.y = metric.BodyColliderY; size.y = metric.BodyColliderHeight; }
            box.center = centre; box.size = size;
            VehiclePopulation.ActualVehiclePhysicalLayout.RecordChassis(box, model);
        }

        /// <summary>
        /// Same original physical wheel allocation/activation, with actual reference
        /// recording. Stock models may retain genuine inactive race-wheel parts.
        /// Box lengths are metres in the vehicle root's local frame.
        /// </summary>
        public static void EnsureRaceWheelColliders(Transform root, int model)
        {
            for (int index = 0; index < PhysicalWheelCount; index++)
            {
                var existing = root.Find("Race wheel collision" + index);
                if (existing)
                {
                    existing.gameObject.SetActive(model == SingleSeatModel);
                    VehiclePopulation.ActualVehiclePhysicalLayout.VerifyRetainedWheel(root, index, existing.GetComponent<BoxCollider>());
                    continue;
                }
                if (model != SingleSeatModel) continue;
                var metric = VehicleDimensions.ForModel(model);
                var wheel = new GameObject("Race wheel collision" + index);
                wheel.layer = root.gameObject.layer;
                wheel.transform.SetParent(root, false);
                var anchor = metric.Anchor(index);
                wheel.transform.localPosition = new Vector3(anchor.x,
                    metric.RestWheelLocalY(GarageRules.Catalogue[model].Mass), anchor.z);
                var box = wheel.AddComponent<BoxCollider>();
                box.size = new Vector3(metric.TyreWidthAt(index), RaceWheelBoxHeightMetres, RaceWheelBoxLengthMetres);
                VehiclePopulation.ActualVehiclePhysicalLayout.RecordAllocatedWheel(root, index, box);
            }
        }
        public static void FollowRaceWheelCollider(Transform root,int model,int index,float y)
        {if(model!=3)return;var t=root.Find("Race wheel collision"+index);if(t){var p=t.localPosition;p.y=y;t.localPosition=p;}}
        public static Vector3 RoadRestPosition(Vector3 surface,int model){var m=VehicleDimensions.ForModel(model);return surface+Vector3.up*m.RestRootHeight(GarageRules.Catalogue[model].Mass);}
        public static Vector3 Anchor(int model,int index){var p=VehicleDimensions.ForModel(model).Anchor(index);return new Vector3(p.x,p.y,p.z);}
        public static Vector3 FromLegacyDentPoint(int model,Vector3 point){point.x*=VehicleDimensions.ForModel(model).LegacyBodyXScale;return point;}
        public static Vector3 ToLegacyDentPoint(int model,Vector3 point){point.x/=VehicleDimensions.ForModel(model).LegacyBodyXScale;return point;}
        // Saved normals are root-space physical impact directions. The root basis
        // remains unchanged; body vertex fitting does not scale these directions.
    }
}
