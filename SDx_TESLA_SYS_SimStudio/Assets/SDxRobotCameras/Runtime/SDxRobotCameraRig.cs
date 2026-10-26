using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDx.SimStudio.Cameras
{
    /// <summary>
    /// Four follow cameras plus one independent Free camera. Follow the moving base_link, not its wrapper.
    /// Only the selected camera renders. No MuJoCo calls, forces, collider edits or robot writes.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("SDx/Robot Follow Cameras")]
    public sealed class SDxRobotCameraRig : MonoBehaviour
    {
        public const string PackageVersion = "1.1.1";

        [Header("Assigned by Create or Update Robot Follow Cameras")]
        [Tooltip("The actual moving base_link. Do not use the imported robot wrapper.")]
        public Transform FollowTarget;
        [Tooltip("Kept intact in Edit mode; suspended only while this rig owns the Game view.")]
        public Camera OriginalCamera;
        public Camera FrontCamera;
        public Camera LeftCamera;
        public Camera RightCamera;
        public Camera BackCamera;
        [Tooltip("Optional saved child camera. If missing, exactly one is created during Play.")]
        public Camera FreeCamera;
        public AudioListener FollowListener;

        [Header("Robot reference frame")]
        [Tooltip("Detected from front_bumper_link minus rear_bumper_link. This Husky uses local +X.")]
        public Vector3 LocalForwardAxis = Vector3.right;
        public SDxRobotCameraView InitialView = SDxRobotCameraView.Front;

        [Header("Framing (metres, relative to base_link)")]
        [Min(0f)] public float LookHeight = 0.80f;
        [Min(0.25f)] public float HorizontalDistance = 2.10f;
        [Min(0.05f)] public float HeightAboveLookPoint = 1.40f;
        [Range(-45f, 45f)] public float AzimuthOffsetDegrees = 0f;
        [Range(20f, 100f)] public float FieldOfView = 60f;
        [Range(0.01f, 0.5f)] public float NearClipPlane = 0.05f;
        [Tooltip("Keep the robot centred in the scene area to the right of the Tools panel.")]
        public bool ReserveToolsPanelSpace = true;

        [Header("Following")]
        [Tooltip("0 = no smoothing. Heading follows yaw only, to avoid camera roll on bumps.")]
        [Range(0f, 30f)] public float PositionDamping = 12f;
        [Range(0f, 30f)] public float HeadingDamping = 12f;
        [Tooltip("Teleport/reset distance that snaps rather than flying through the house.")]
        [Min(0.1f)] public float ResetSnapDistance = 0.8f;
        public bool ShowCameraGuides = false;

        [Header("Free Camera (Game view only)")]
        [Min(0.05f)] public float FreeMoveSpeed = 2f;
        [Range(1f, 10f)] public float FreeFastMultiplier = 3f;
        [Range(0.1f, 10f)] public float FreeMouseSensitivity = 2f;
        [Min(0.01f)] public float FreeWheelDistance = 0.5f;
        [Tooltip("Q/E change world height; WASD fly along the current camera axes.")]
        public bool FreeVerticalKeys = true;
        [Tooltip("Discard cursor-centering input after EACH right-click lock. Does not reset global input axes.")]
        [Range(0.05f, 0.5f)] public float FreeLookSettleSeconds = 0.12f;

        // Set by the existing UI bridge before LateUpdate. Fail closed if absent.
        private bool freeKeyboardBlocked = true, freePointerBlocked = true;
        private bool ownsCursor, lookRequested;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private int freeEnteredFrame = -1;
        private readonly SDxFreeLookInputGate freeLookGate = new SDxFreeLookInputGate();
        private float freeYaw, freePitch;

        public SDxRobotCameraView ActiveView { get { return activeView; } }
        public bool IsReady { get { return ownsView; } }
        public Camera ActiveCamera { get { return GetCamera(activeView); } }
        public event Action<SDxRobotCameraView> ViewChanged;

        private SDxRobotCameraView activeView = SDxRobotCameraView.Front;
        private bool ownsView, poseValid;
        private Vector3 smoothedFocus, lastTargetPosition;
        private Quaternion smoothedHeading = Quaternion.identity;
        private int snapFrames;
        private Rect viewport = new Rect(0, 0, 1, 1);
        private readonly List<Camera> suspendedCameras = new List<Camera>();
        private readonly List<AudioListener> suspendedListeners = new List<AudioListener>();

        public Camera GetCamera(SDxRobotCameraView view)
        {
            switch (view)
            {
                case SDxRobotCameraView.Left: return LeftCamera;
                case SDxRobotCameraView.Right: return RightCamera;
                case SDxRobotCameraView.Back: return BackCamera;
                case SDxRobotCameraView.Free: return FreeCamera;
                default: return FrontCamera;
            }
        }

        public bool ValidateReferences(out string message)
        {
            if (FollowTarget == null || OriginalCamera == null || FollowListener == null)
            { message = "Missing moving body, original camera or audio listener. Run the camera setup menu."; return false; }
            if (LocalForwardAxis.sqrMagnitude < 0.000001f)
            { message = "Local Forward Axis must not be zero."; return false; }
            var ids = new HashSet<int>();
            for (int i = 0; i < 4; ++i)
            {
                Camera c = GetCamera((SDxRobotCameraView)i);
                if (c == null || c == OriginalCamera || !c.transform.IsChildOf(transform) || !ids.Add(c.GetInstanceID()))
                { message = "Expected four distinct child cameras, separate from the original camera."; return false; }
            }
            if (FreeCamera != null && (FreeCamera == OriginalCamera ||
                !FreeCamera.transform.IsChildOf(transform) || !ids.Add(FreeCamera.GetInstanceID())))
            { message = "Free Camera must be a separate child camera, not one of the follow cameras."; return false; }
            if (ownsView && FreeCamera == null)
            { message = "The runtime Free Camera is missing."; return false; }
            if (!FollowListener.transform.IsChildOf(transform) || FollowTarget.IsChildOf(transform))
            { message = "Only camera/listener objects may belong to the camera rig. The robot must be outside it."; return false; }
            message = "References OK: four follow cameras + one listener. " +
                (FreeCamera == null ? "Free Camera will be created during Play." : "Free Camera is present.");
            return true;
        }

        private void OnValidate()
        {
            LookHeight = Mathf.Max(0, LookHeight);
            HorizontalDistance = Mathf.Max(0.25f, HorizontalDistance);
            HeightAboveLookPoint = Mathf.Max(0.05f, HeightAboveLookPoint);
            ResetSnapDistance = Mathf.Max(0.1f, ResetSnapDistance);
            if ((int)InitialView < 0 || (int)InitialView > 3) InitialView = SDxRobotCameraView.Front;
            FieldOfView = Mathf.Clamp(FieldOfView, 20f, 100f);
            NearClipPlane = Mathf.Clamp(NearClipPlane, 0.01f, 0.5f);
            FreeMoveSpeed = Mathf.Max(.05f, FreeMoveSpeed);
            FreeFastMultiplier = Mathf.Clamp(FreeFastMultiplier, 1f, 10f);
            FreeMouseSensitivity = Mathf.Clamp(FreeMouseSensitivity, .1f, 10f);
            FreeWheelDistance = Mathf.Max(.01f, FreeWheelDistance);
            FreeLookSettleSeconds = Mathf.Clamp(FreeLookSettleSeconds, .05f, .5f);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            string error;
            if (!ValidateReferences(out error)) { Debug.LogError("[SDx Cameras] " + error, this); return; }
            EnsureRuntimeFreeCamera();
            TakeViewOwnership();
        }

        private void TakeViewOwnership()
        {
            if (ownsView) return;
            // Sensor cameras rendering into textures / other displays are not suspended.
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (c == null || c.gameObject.scene != gameObject.scene || !c.isActiveAndEnabled || IsOwnedCamera(c)) continue;
                if (c == OriginalCamera || (c.cameraType == CameraType.Game && c.targetTexture == null &&
                    c.targetDisplay == OriginalCamera.targetDisplay))
                { suspendedCameras.Add(c); c.enabled = false; }
            }
            foreach (AudioListener listener in Resources.FindObjectsOfTypeAll<AudioListener>())
            {
                if (listener != null && listener != FollowListener && listener.gameObject.scene == gameObject.scene &&
                    listener.isActiveAndEnabled)
                { suspendedListeners.Add(listener); listener.enabled = false; }
            }
            ownsView = true;
            activeView = (int)InitialView >= 0 && (int)InitialView <= 3 ? InitialView : SDxRobotCameraView.Front;
            poseValid = false;
            RequestSnap(4);
            UpdatePoses(true);
            ApplySelectedView();
            Debug.Log("[SDx Cameras] Four follow cameras + Free ready (1.1.1, guarded RMB input). Default view: " + activeView +
                ". Target: " + FollowTarget.name + ". Camera-only; robot physics unchanged.", this);
        }

        private bool IsOwnedCamera(Camera c)
        { return c == FrontCamera || c == LeftCamera || c == RightCamera || c == BackCamera || c == FreeCamera; }

        private void OnDisable() { ReleaseViewOwnership(); }
        private void OnDestroy() { ReleaseViewOwnership(); }

        private void ReleaseViewOwnership()
        {
            ReleaseFreeLook();
            lookRequested = false;
            if (!ownsView) return;
            ownsView = false;
            for (int i = 0; i < 5; ++i) { Camera c = GetCamera((SDxRobotCameraView)i); if (c != null) c.enabled = false; }
            if (FollowListener != null) FollowListener.enabled = false;
            foreach (Camera c in suspendedCameras) if (c != null) c.enabled = true;
            foreach (AudioListener listener in suspendedListeners) if (listener != null) listener.enabled = true;
            suspendedCameras.Clear(); suspendedListeners.Clear();
            poseValid = false;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !ownsView) return;
            if (FollowTarget == null || !FollowTarget.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[SDx Cameras] Follow target unavailable. Restoring the original Game view.", this);
                ReleaseViewOwnership(); return;
            }
            UpdatePoses(false);
            if (activeView == SDxRobotCameraView.Free) UpdateFreeCamera();
            MoveListener();
        }

        // Scenario resets update only the follow cameras, never teleport a Free view.
        public void RequestSnap(int frames = 3) { snapFrames = Mathf.Max(snapFrames, Mathf.Max(1, frames)); }

        public void SelectView(SDxRobotCameraView view)
        {
            if ((int)view < 0 || (int)view > 4 || view == activeView) return;
            if (Application.isPlaying && ownsView)
            {
                if (view == SDxRobotCameraView.Free)
                {
                    // Copy the displayed pose BEFORE advancing/smoothing follow cameras.
                    Camera source = ActiveCamera;
                    EnsureRuntimeFreeCamera();
                    if (source == null || FreeCamera == null) return;
                    CopyFreeView(source);
                    freeEnteredFrame = Time.frameCount;
                }
                ReleaseFreeLook(); lookRequested = false;
                activeView = view;
                // Follow views return immediately to the robot's current pose.
                if (view != SDxRobotCameraView.Free) UpdatePoses(true);
                ApplySelectedView();
            }
            else if (view != SDxRobotCameraView.Free) activeView = view;
        }

        public void SelectView(string view)
        {
            SDxRobotCameraView parsed;
            if (Enum.TryParse(view, true, out parsed)) SelectView(parsed);
        }

        private void ApplySelectedView()
        {
            for (int i = 0; i < 5; ++i)
            {
                Camera c = GetCamera((SDxRobotCameraView)i);
                if (c != null) c.enabled = i == (int)activeView;
            }
            MoveListener();
            FollowListener.enabled = true;
            if (ViewChanged != null) ViewChanged(activeView);
        }

        public void SetViewport(Rect rect)
        {
            float x = Mathf.Clamp(rect.x, 0, 0.9f), y = Mathf.Clamp(rect.y, 0, 0.9f);
            viewport = new Rect(x, y, Mathf.Clamp(rect.width, 0.1f, 1 - x), Mathf.Clamp(rect.height, 0.1f, 1 - y));
            for (int i = 0; i < 5; ++i) { Camera c = GetCamera((SDxRobotCameraView)i); if (c != null) c.rect = viewport; }
        }

        // Called explicitly in Edit mode by the installer; no automatic scene mutation in Edit mode.
        public void PreviewNow()
        {
            UpdatePoses(true);
            if (!Application.isPlaying)
            {
                for (int i = 0; i < 5; ++i) { Camera c = GetCamera((SDxRobotCameraView)i); if (c != null) c.enabled = false; }
                if (FollowListener != null) FollowListener.enabled = false;
            }
        }

        private void UpdatePoses(bool forceSnap)
        {
            if (FollowTarget == null) return;
            Vector3 rawPosition = FollowTarget.position;
            Vector3 rawFocus = rawPosition + Vector3.up * LookHeight;
            Vector3 previousForward = poseValid ? smoothedHeading * Vector3.forward : Vector3.right;
            Vector3 flatForward = SDxCameraPoseMath.PlanarForward(FollowTarget.rotation, LocalForwardAxis, previousForward);
            Quaternion targetHeading = Quaternion.LookRotation(flatForward, Vector3.up);
            bool snap = forceSnap || !poseValid || snapFrames > 0 ||
                (rawPosition - lastTargetPosition).sqrMagnitude > ResetSnapDistance * ResetSnapDistance;
            if (snap)
            { smoothedFocus = rawFocus; smoothedHeading = targetHeading; }
            else
            {
                smoothedFocus = Vector3.Lerp(smoothedFocus, rawFocus, SDxCameraPoseMath.FollowWeight(PositionDamping, Time.unscaledDeltaTime));
                smoothedHeading = Quaternion.Slerp(smoothedHeading, targetHeading, SDxCameraPoseMath.FollowWeight(HeadingDamping, Time.unscaledDeltaTime));
            }
            lastTargetPosition = rawPosition; poseValid = true;
            if (snapFrames > 0) --snapFrames;
            Vector3 forward = smoothedHeading * Vector3.forward;
            for (int i = 0; i < 4; ++i)
            {
                Camera c = GetCamera((SDxRobotCameraView)i);
                if (c == null) continue;
                Pose pose = SDxCameraPoseMath.ViewPose(smoothedFocus, forward, (SDxRobotCameraView)i,
                    HorizontalDistance, HeightAboveLookPoint, AzimuthOffsetDegrees);
                c.transform.SetPositionAndRotation(pose.position, pose.rotation);
                c.orthographic = false; c.fieldOfView = FieldOfView; c.nearClipPlane = NearClipPlane;
                c.rect = viewport;
            }
            MoveListener();
        }

        private void EnsureRuntimeFreeCamera()
        {
            if (FreeCamera != null) return;
            // A runtime-created child disappears normally when Play exits.
            // No scene file is edited and no component is added to the robot.
            var child = new GameObject("Camera_Free");
            child.transform.SetParent(transform, false);
            child.tag = "MainCamera";
            FreeCamera = child.AddComponent<Camera>();
            Camera source = FrontCamera != null ? FrontCamera : OriginalCamera;
            if (source != null) CopyFreeView(source);
            FreeCamera.enabled = false;
        }

        private void CopyFreeView(Camera source)
        {
            FreeCamera.CopyFrom(source);
            FreeCamera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            FreeCamera.targetTexture = null;
            FreeCamera.rect = source.rect;
            FreeCamera.ResetWorldToCameraMatrix();
            FreeCamera.ResetProjectionMatrix();
            FreeCamera.enabled = false;
            Skybox sourceSky = source.GetComponent<Skybox>();
            Skybox freeSky = FreeCamera.GetComponent<Skybox>();
            if (sourceSky != null)
            {
                if (freeSky == null) freeSky = FreeCamera.gameObject.AddComponent<Skybox>();
                freeSky.material = sourceSky.material; freeSky.enabled = sourceSky.enabled;
            }
            else if (freeSky != null) freeSky.enabled = false;
            SDxFreeCameraMath.ReadLookAngles(FreeCamera.transform.rotation, out freeYaw, out freePitch);
        }

        public void SetFreeInputBlocked(bool keyboardBlocked, bool pointerBlocked)
        {
            freeKeyboardBlocked = keyboardBlocked; freePointerBlocked = pointerBlocked;
            if (keyboardBlocked || pointerBlocked) ReleaseFreeLook();
            if (pointerBlocked) lookRequested = false;
        }

        private static bool HasGameInputFocus()
        {
            if (!Application.isFocused) return false;
#if UNITY_EDITOR
            // Never move the Game camera while the user works in Scene/Inspector/Console.
            var focused = UnityEditor.EditorWindow.focusedWindow;
            return focused != null && focused.GetType().Name == "GameView";
#else
            return true;
#endif
        }

        private void UpdateFreeCamera()
        {
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            if (FreeCamera == null) return;
            if (!HasGameInputFocus() || Time.frameCount <= freeEnteredFrame)
            { ReleaseFreeLook(); lookRequested = false; return; }
            if (Input.GetKeyDown(KeyCode.Escape))
            { ReleaseFreeLook(); lookRequested = false; return; }
            if (!Input.GetMouseButton(1)) { ReleaseFreeLook(); lookRequested = false; }
            // A click outside a text field is allowed to release its focus in OnGUI.
            // The pending look begins after that focus clears, not while typing.
            bool inside = FreeCamera.pixelRect.Contains(Input.mousePosition);
            if (Input.GetMouseButtonDown(1) && inside && !freePointerBlocked) lookRequested = true;
            if (freeKeyboardBlocked || freePointerBlocked)
            { ReleaseFreeLook(); return; }
            if (!inside && !ownsCursor) { lookRequested = false; return; }
            if (lookRequested && Input.GetMouseButton(1) && !ownsCursor)
            {
                previousCursorLock = Cursor.lockState; previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                ownsCursor = true;
                // A lock can generate delayed warp deltas, not just on this frame.
                freeLookGate.Begin(Time.frameCount, Time.unscaledTime);
                SDxFreeCameraMath.ReadLookAngles(FreeCamera.transform.rotation, out freeYaw, out freePitch);
            }
            if (ownsCursor && Cursor.lockState != CursorLockMode.Locked)
            { ReleaseFreeLook(); lookRequested = false; return; }
            if (ownsCursor)
            {
                // Read once per frame, but never turn cursor warping into camera motion.
                // No Input.ResetInputAxes(): robot/UI input must not be reset globally.
                float yawDelta, pitchDelta;
                if (freeLookGate.TryRead(Time.frameCount, Time.unscaledTime,
                    Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"),
                    FreeMouseSensitivity, FreeLookSettleSeconds, out yawDelta, out pitchDelta))
                {
                    float nextPitch = Mathf.Clamp(freePitch + pitchDelta, -85f, 85f);
                    float appliedPitch = nextPitch - freePitch;
                    freePitch = nextPitch;
                    freeYaw = Mathf.Repeat(freeYaw + yawDelta + 180f, 360f) - 180f;
                    // Do NOT reconstruct Euler angles on button-down or zero-input frames.
                    // Increment the current quaternion, preserving the inherited pose/roll.
                    FreeCamera.transform.rotation =
                        Quaternion.AngleAxis(yawDelta, Vector3.up) * FreeCamera.transform.rotation *
                        Quaternion.AngleAxis(appliedPitch, Vector3.right);
                }
            }
            float lateral = (Input.GetKey(KeyCode.D) ? 1f : 0) - (Input.GetKey(KeyCode.A) ? 1f : 0);
            float longitudinal = (Input.GetKey(KeyCode.W) ? 1f : 0) - (Input.GetKey(KeyCode.S) ? 1f : 0);
            float vertical = FreeVerticalKeys
                ? (Input.GetKey(KeyCode.E) ? 1f : 0) - (Input.GetKey(KeyCode.Q) ? 1f : 0) : 0;
            float multiplier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? FreeFastMultiplier : 1f;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0, .05f);
            Vector3 motion = SDxFreeCameraMath.FlyDirection(FreeCamera.transform.rotation,
                lateral, vertical, longitudinal) * Mathf.Max(.05f, FreeMoveSpeed) * multiplier * dt;
            // Dolly zoom: move along the view direction; preserve the inherited FOV.
            float scroll = Mathf.Clamp(Input.mouseScrollDelta.y, -10f, 10f);
            motion += FreeCamera.transform.forward * scroll * Mathf.Max(.01f, FreeWheelDistance);
            FreeCamera.transform.position += motion;
#endif
        }

        private void ReleaseFreeLook()
        {
            freeLookGate.End();
            if (!ownsCursor) return;
            ownsCursor = false;
            if (Cursor.lockState == CursorLockMode.Locked) Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        private void OnApplicationFocus(bool focused)
        { if (!focused) { ReleaseFreeLook(); lookRequested = false; } }

        private void OnApplicationPause(bool paused)
        { if (paused) { ReleaseFreeLook(); lookRequested = false; } }

        private void MoveListener()
        {
            Camera c = ActiveCamera;
            if (FollowListener != null && c != null)
                FollowListener.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
        }

        private void OnDrawGizmosSelected()
        {
            if (!ShowCameraGuides || FollowTarget == null) return;
            Vector3 focus = FollowTarget.position + Vector3.up * LookHeight;
            Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(focus, 0.06f);
            for (int i = 0; i < 4; ++i)
            {
                Camera c = GetCamera((SDxRobotCameraView)i);
                if (c == null) continue;
                Gizmos.DrawLine(c.transform.position, focus);
                Gizmos.DrawWireSphere(c.transform.position, 0.05f);
            }
        }
    }
}
