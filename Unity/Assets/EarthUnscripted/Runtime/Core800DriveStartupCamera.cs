using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EarthUnscripted
{
    /// <summary>
    /// An explicitly opt-in QA loading camera. It clears a real URP target before
    /// scene loading yields, without an AudioListener, MainCamera tag or world draw.
    /// The proof owns this component and retires its clearing camera only after
    /// endCameraRendering reports the actual chase camera rendered. It changes no
    /// production pipeline, project settings, physics or source geometry.
    /// </summary>
    internal sealed class Core800DriveStartupCamera : MonoBehaviour
    {
        private const float ClearingDepth = -100;
        private const float NearClipMetres = 0.1f;
        private const float FarClipMetres = 1;
        private Camera clearingCamera;
        private Camera actualDriveCamera;
        private bool disposed;
        internal bool Created { get; private set; }
        internal int ClearingRenderCount { get; private set; }
        internal bool DriveCameraRendered { get; private set; }
        internal bool RetiredAfterDriveRender { get; private set; }

        /// <summary>
        /// Main-thread creation, called only after the parent proof validates its
        /// explicit command flag, isolated QA product and source/build binding.
        /// The returned owner must be disposed even on preparation failure.
        /// </summary>
        internal static Core800DriveStartupCamera Create()
        {
            var owner = new GameObject("QA-only loading camera owner");
            DontDestroyOnLoad(owner);
            var lifecycle = owner.AddComponent<Core800DriveStartupCamera>();
            try
            {
                var cameraObject = new GameObject("QA-only URP clear; no audio or main-camera tag");
                cameraObject.transform.SetParent(owner.transform, false);
                lifecycle.clearingCamera = cameraObject.AddComponent<Camera>();
                lifecycle.clearingCamera.depth = ClearingDepth;
                lifecycle.clearingCamera.cullingMask = 0;
                lifecycle.clearingCamera.clearFlags = CameraClearFlags.SolidColor;
                lifecycle.clearingCamera.backgroundColor = new Color(0.035f, 0.045f, 0.06f, 1);
                lifecycle.clearingCamera.nearClipPlane = NearClipMetres;
                lifecycle.clearingCamera.farClipPlane = FarClipMetres;
                lifecycle.clearingCamera.allowHDR = false;
                var additional = lifecycle.clearingCamera.GetUniversalAdditionalCameraData();
                additional.renderType = CameraRenderType.Base;
                additional.renderPostProcessing = false;
                additional.renderShadows = false;
                RenderPipelineManager.endCameraRendering += lifecycle.CameraRendered;
                lifecycle.Created = true;
                return lifecycle;
            }
            catch
            {
                lifecycle.DisposeOwned();
                throw;
            }
        }

        /// <summary>Register the real chase camera without disabling the loading camera.</summary>
        internal void ObserveDriveCamera(Camera camera)
        {
            if (disposed || !camera || camera == clearingCamera)
                throw new InvalidOperationException("Actual owned drive camera required");
            actualDriveCamera = camera;
        }

        private void CameraRendered(ScriptableRenderContext context, Camera renderedCamera)
        {
            if (renderedCamera == clearingCamera)
                ClearingRenderCount++;
            if (renderedCamera != actualDriveCamera || !actualDriveCamera)
                return;
            DriveCameraRendered = true;
            if (clearingCamera)
            {
                // Actual render completion, not camera creation or an assumed frame delay.
                clearingCamera.enabled = false;
                Destroy(clearingCamera.gameObject);
                clearingCamera = null;
                RetiredAfterDriveRender = true;
            }
        }

        internal void DisposeOwned()
        {
            if (disposed)
                return;
            disposed = true;
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            if (clearingCamera)
                clearingCamera.enabled = false;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            disposed = true;
        }
    }
}
