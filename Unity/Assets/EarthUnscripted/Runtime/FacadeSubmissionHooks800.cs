#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted.Scenery800.Diagnostics
{
    /// <summary>
    /// Optional synchronous diagnostic boundary, default OFF. This is not source,
    /// placement or rendering authority. Observers must never mutate draw inputs,
    /// materials, meshes, cameras or owners. Borrowed matrices are valid only until
    /// the callback returns and must not be retained across a yield.
    /// </summary>
    public static class FacadeSubmissionHooks800
    {
        public interface IObserver
        {
            void Queued(BuildingFacadeChunk chunk, int group, int slot, int featureIndex,
                BuildingFacadeChunk.Feature feature, Matrix4x4 root, Matrix4x4 actualMatrix,
                Vector3 actualPosition, Bounds actualBounds, Camera capturedCamera, int frame);
            void Flushing(BuildingFacadeChunk chunk, int group, Mesh mesh, Material material,
                Matrix4x4[] borrowedMatrices, int count, Bounds wholeGroupBounds,
                Camera capturedCamera, bool instanced, ShadowCastingMode shadow,
                RenderParams? actualRenderParameters, int frame);
            void ObserverFault(string reason);
        }

        public static IObserver Current { get; private set; }
        public static bool TryInstall(IObserver observer)
        {
            if (observer == null || Current != null) return false;
            Current = observer;
            return true;
        }
        public static bool Remove(IObserver observer)
        {
            if (!ReferenceEquals(Current, observer)) return false;
            Current = null;
            return true;
        }

        public static void NotifyQueued(IObserver observer, BuildingFacadeChunk chunk, int group,
            int slot, int featureIndex, BuildingFacadeChunk.Feature feature, Matrix4x4 root,
            Matrix4x4 actualMatrix, Vector3 actualPosition, Bounds actualBounds, Camera camera, int frame)
        {
            try { observer.Queued(chunk, group, slot, featureIndex, feature, root,
                actualMatrix, actualPosition, actualBounds, camera, frame); }
            catch (Exception error) { Fault(observer, error); }
        }
        public static void NotifyFlushing(IObserver observer, BuildingFacadeChunk chunk, int group,
            Mesh mesh, Material material, Matrix4x4[] matrices, int count, Bounds bounds,
            Camera camera, bool instanced, ShadowCastingMode shadow, RenderParams? parameters, int frame)
        {
            try { observer.Flushing(chunk, group, mesh, material, matrices, count,
                bounds, camera, instanced, shadow, parameters, frame); }
            catch (Exception error) { Fault(observer, error); }
        }
        private static void Fault(IObserver observer, Exception error)
        {
            try { observer.ObserverFault("Observer exception: " + error.GetType().Name); }
            catch { /* A diagnostic fault must never change the original draw path. */ }
        }
    }
}

#endif
