using System;
using EarthUnscripted.Core;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Size-dependent trunk response. Saplings can break and stop
    /// resisting the car; mature trees remain solid. The owner also removes the
    /// corresponding instanced standing tree for the current world session.</summary>
    public sealed class TreeImpact : MonoBehaviour
    {
        public float RadiusMetres { get; private set; }
        public float EffectiveMassKg => VehicleImpactRules.TreeEffectiveMass(RadiusMetres);
        public bool Broken { get; private set; }
        Collider trunk;
        Action broken;
        public void Configure(float radius, Action onBroken)
        {
            RadiusMetres = radius; broken = onBroken; Broken = false;
            trunk = GetComponent<Collider>();
        }
        public float Strike(float carMass, float closingSpeed, Vector3 direction)
        {
            if (Broken) return .05f;
            float energy = .5f * carMass * closingSpeed * closingSpeed;
            if (RadiusMetres <= .20f && energy > VehicleImpactRules.TreeBreakEnergy(RadiusMetres))
            {
                Broken = true; trunk.enabled = false; broken?.Invoke();
                var wood = Geometry.Material("Broken tree wood", new Color(.27f,.18f,.09f));
                var log = Geometry.Primitive(PrimitiveType.Cylinder, "Fallen sapling", null, transform.position + Vector3.up * .3f,
                    new Vector3(RadiusMetres * 2, 1.3f, RadiusMetres * 2), wood, false);
                log.transform.rotation = Quaternion.FromToRotation(Vector3.up, (direction + Vector3.up * .05f).normalized);
                Destroy(log, 18);
                return Mathf.Lerp(.08f, .35f, RadiusMetres / .20f);
            }
            return Mathf.Lerp(.45f, 1.5f, Mathf.InverseLerp(.08f, .55f, RadiusMetres));
        }
    }
}
