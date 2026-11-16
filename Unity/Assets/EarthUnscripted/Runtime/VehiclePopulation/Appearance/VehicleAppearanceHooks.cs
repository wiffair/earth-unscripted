using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EarthUnscripted.VehiclePopulation
{
    /// <summary>
    /// Actual writer hooks extending the existing producer hook type. No alternate
    /// registry/discovery, factory ticket, complete flag or native object mutation.
    /// Scope BEGIN precedes source writes; Dispose ends only managed bookkeeping.
    /// Internal callers classify mesh-content versus observable pose/text/LOD writes.
    /// Root ancestor association uses the existing actual watcher, never names/models.
    /// Unknown hook errors retire appearance only and must not interrupt original game.
    /// </summary>
    internal static partial class VehiclePopulationHooks
    {
        internal static IDisposable BeforeAppearanceContent(Transform actualSource) =>
            BeforeAppearanceRoot(actualSource, true);
        internal static IDisposable BeforeAppearancePose(Transform actualSource) =>
            BeforeAppearanceRoot(actualSource, false);

        private static IDisposable BeforeAppearanceRoot(Transform source, bool content)
        {
            var globalScopes = content ? new List<IDisposable>() : null;
            try
            {
                // ALL actual owners enter the global fence BEFORE the first native
                // ancestry getter, including roots with no borrower index yet.
                if (content)
                    foreach (var population in populations.ToArray())
                        globalScopes.Add(population.BeginKnownAppearanceWrite());
                if (source)
                {
                    var watcher = source.GetComponentInParent<ActualVehiclePresence>(true);
                    IDisposable localScope = watcher?.Population?.BeginAppearanceWrite(watcher.gameObject, content);
                    if (!content) return localScope;
                    globalScopes.Add(localScope);
                }
                return content ? new AppearanceScopeSet(globalScopes) : null;
            }
            catch
            {
                foreach (var population in populations.ToArray()) population.PoisonAppearanceDomain();
                if (globalScopes != null) new AppearanceScopeSet(globalScopes).Dispose();
                return null;
            }
        }

        /// <summary>
        /// Route one actual Mesh reference to EVERY attached population before scratch
        /// or buffer writes/destruction. Private borrower indexes contain all loaded
        /// inactive aliases observed during cold capture, not only enabled LOD meshes.
        /// The global population list is the existing actual-owner attachment list.
        /// </summary>
        internal static IDisposable BeforeAppearanceMesh(Mesh actualMesh)
        {
            var scopes = new List<IDisposable>();
            try
            {
                var actualOwners = populations.ToArray();
                foreach (var population in actualOwners) scopes.Add(population.BeginKnownAppearanceWrite());
                foreach (var population in actualOwners) scopes.Add(population.BeginBorrowedMeshWrite(actualMesh));
                return new AppearanceScopeSet(scopes);
            }
            catch
            {
                foreach (var population in populations.ToArray()) population.PoisonAppearanceDomain();
                new AppearanceScopeSet(scopes).Dispose();
                return null;
            }
        }

        /// <summary>
        /// Engine font rebuild is a POST-native callback, not a before-native fence.
        /// Before this source callback changes the shared material, retire outstanding
        /// observations. Fresh native TextMesh bounds/signature must still validate;
        /// actual raster/font regeneration agreement remains a separate native gate.
        /// </summary>
        internal static IDisposable BeforeAppearanceFontCallback()
        {
            var scopes = new List<IDisposable>();
            try
            {
                var actualOwners = populations.ToArray();
                foreach (var population in actualOwners) scopes.Add(population.BeginKnownAppearanceWrite());
                foreach (var population in actualOwners) scopes.Add(population.BeginTextNativeChange());
                return new AppearanceScopeSet(scopes);
            }
            catch
            {
                foreach (var population in populations.ToArray()) population.PoisonAppearanceDomain();
                new AppearanceScopeSet(scopes).Dispose();
                return null;
            }
        }
    }
}
