using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadRush
{
    public sealed partial class GameSession
    {
        SessionBootClearingCamera bootClearingCamera;
        bool bootCameraOwnerDisposed;
        string bootCameraIssue = "";

        /// <summary>Render diagnostics only, never boot/world readiness authority.</summary>
        public bool BootClearingCameraPresent => bootClearingCamera && bootClearingCamera.ClearingPresent;
        public int BootClearingCameraRenderCount => bootClearingCamera ? bootClearingCamera.ClearingRenderCount : 0;
        public bool BootDriveCameraRenderConfirmed => bootClearingCamera && bootClearingCamera.DriveRenderConfirmed;
        public string BootClearingCameraIssue => bootCameraIssue;

        /// <summary>
        /// Main thread, before profile loading/first boot yield. The child belongs
        /// to the session, outside BootSceneOwnership's rollback root allocations.
        /// Allocation failure is diagnostic; it must not block career/boot behavior.
        /// </summary>
        void EnsureBootClearingCamera()
        {
            if (bootCameraOwnerDisposed || bootClearingCamera) return;
            try { bootClearingCamera = SessionBootClearingCamera.Create(this); }
            catch (Exception exception)
            {
                bootCameraIssue = exception.GetType().Name + ": " + exception.Message;
                Debug.LogWarning("Loading camera unavailable: " + bootCameraIssue);
            }
        }

        void BeginBootClearingCamera(int generation)
        {
            EnsureBootClearingCamera();
            if (!bootClearingCamera) return;
            try { bootClearingCamera.Begin(generation); }
            catch (Exception exception)
            {
                bootCameraIssue = exception.GetType().Name + ": " + exception.Message;
                Debug.LogWarning("Loading camera could not rearm: " + bootCameraIssue);
                bootClearingCamera.DisposeOwned();
                bootClearingCamera = null;
            }
        }

        /// <summary>Rearm for cancellation/failure; the unchanged boot menu still renders.</summary>
        void KeepBootClearingCamera()
        {
            if (!bootCameraOwnerDisposed) BeginBootClearingCamera(bootGeneration);
        }

        /// <summary>Called inside the no-yield Ready commit, after actor activation.</summary>
        void ObserveReadyBootDriveCamera(int generation)
        {
            if (bootCameraOwnerDisposed || !bootClearingCamera || !BootReady || !View) return;
            var actualCamera = View.GetComponent<Camera>();
            if (!bootClearingCamera.ObserveReadyDrive(generation, actualCamera))
                bootCameraIssue = "Current ready drive camera was not eligible for render handoff.";
        }

        void DisposeBootClearingCamera()
        {
            // Set first: CancelBootWorker during OnDestroy must not create a new child.
            bootCameraOwnerDisposed = true;
            if (bootClearingCamera) bootClearingCamera.DisposeOwned();
            bootClearingCamera = null;
        }
    }

    /// <summary>
    /// Session-owned URP target clearer. No audio, MainCamera tag, world culling,
    /// DontDestroyOnLoad, settings, career writes or extra OnGUI. The existing
    /// GameSession/LocationLoading menus retain their original behavior and order.
    /// This does not make synchronous Awake work render before the engine yields.
    /// </summary>
    internal sealed class SessionBootClearingCamera : MonoBehaviour
    {
        const float ClearingDepth = -100; // Behind the real depth-zero driving view.
        const float NearClipMetres = 0.1f;
        const float FarClipMetres = 1;
        readonly BootCameraHandoffState state = new BootCameraHandoffState();
        GameSession owner;
        Camera clearing;
        Camera expectedDrive;
        int expectedGeneration;
        bool subscribed;

        internal bool ClearingPresent => clearing && clearing.enabled;
        internal int ClearingRenderCount { get; private set; }
        internal bool DriveRenderConfirmed => state.RenderConfirmed;

        internal static SessionBootClearingCamera Create(GameSession actualOwner)
        {
            if (!actualOwner) throw new ArgumentNullException(nameof(actualOwner));
            var root = new GameObject("Session loading camera owner");
            root.transform.SetParent(actualOwner.transform, false);
            SessionBootClearingCamera lifecycle = null;
            try
            {
                lifecycle = root.AddComponent<SessionBootClearingCamera>();
                lifecycle.owner = actualOwner;
                lifecycle.Begin(0);
                return lifecycle;
            }
            catch
            {
                if (lifecycle) lifecycle.DisposeOwned();
                else Destroy(root);
                throw;
            }
        }

        /// <summary>Rearms without adopting or disabling any existing drive camera.</summary>
        internal void Begin(int generation)
        {
            state.Begin(generation);
            expectedDrive = null;
            expectedGeneration = generation;
            if (!clearing)
            {
                var cameraObject = new GameObject("Loading clear only; no audio or MainCamera tag");
                cameraObject.transform.SetParent(transform, false);
                clearing = cameraObject.AddComponent<Camera>();
                clearing.depth = ClearingDepth;
                clearing.cullingMask = 0;
                clearing.clearFlags = CameraClearFlags.SolidColor;
                clearing.backgroundColor = new Color(0.025f, 0.04f, 0.06f, 1);
                clearing.nearClipPlane = NearClipMetres;
                clearing.farClipPlane = FarClipMetres;
                clearing.allowHDR = false;
                var additional = clearing.GetUniversalAdditionalCameraData();
                additional.renderType = CameraRenderType.Base;
                additional.renderPostProcessing = false;
                additional.renderShadows = false;
            }
            clearing.enabled = owner && owner.isActiveAndEnabled && isActiveAndEnabled;
            Subscribe();
        }

        internal bool ObserveReadyDrive(int generation, Camera actualDrive)
        {
            if (!owner || !owner.BootReady || !owner.View || !actualDrive ||
                actualDrive != owner.View.GetComponent<Camera>() || actualDrive == clearing ||
                !actualDrive.isActiveAndEnabled || actualDrive.targetTexture ||
                !state.ObserveCurrentDrive(generation))
                return false;
            expectedDrive = actualDrive;
            expectedGeneration = generation;
            return true;
        }

        void Subscribe()
        {
            if (subscribed || state.Disposed || !owner || !isActiveAndEnabled) return;
            RenderPipelineManager.endCameraRendering += CameraRendered;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            subscribed = false;
        }

        void CameraRendered(ScriptableRenderContext context, Camera rendered)
        {
            if (rendered == clearing) ClearingRenderCount++;
            if (!owner || !owner.isActiveAndEnabled || !owner.BootReady || !owner.View ||
                rendered != expectedDrive || rendered != owner.View.GetComponent<Camera>() ||
                !rendered || !rendered.isActiveAndEnabled || rendered.targetTexture ||
                !state.ConfirmCurrentRender(expectedGeneration))
                return;
            // Rendering, not creation/Ready or an assumed number of frames, retires it.
            if (clearing)
            {
                clearing.enabled = false;
                Destroy(clearing.gameObject);
                clearing = null;
            }
            expectedDrive = null;
            Unsubscribe();
        }

        void Update()
        {
            if (!owner)
            {
                DisposeOwned();
                return;
            }
            if (clearing)
                clearing.enabled = state.Waiting && owner.isActiveAndEnabled && isActiveAndEnabled;
        }

        void OnEnable() => Subscribe();
        void OnDisable()
        {
            if (clearing) clearing.enabled = false;
            Unsubscribe();
        }

        internal void DisposeOwned()
        {
            if (state.Disposed) return;
            state.Dispose();
            Unsubscribe();
            if (clearing) clearing.enabled = false;
            expectedDrive = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            state.Dispose();
            Unsubscribe();
            // Also cover destruction of this component alone: no stranded child
            // may keep clearing behind an otherwise live session indefinitely.
            if (clearing)
            {
                clearing.enabled = false;
                Destroy(clearing.gameObject);
                clearing = null;
            }
        }
    }
}
