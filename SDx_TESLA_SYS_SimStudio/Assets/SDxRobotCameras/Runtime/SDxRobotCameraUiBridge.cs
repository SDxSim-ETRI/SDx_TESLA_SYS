using UnityEngine;
using SDx.SimStudio.UI;
using SDx.SimStudio.Logging;

namespace SDx.SimStudio.Cameras
{
    /// <summary>Uses GUI camera events and accepted scenario dispatch; never writes physics state.</summary>
    [DefaultExecutionOrder(31000)]
    [DisallowMultipleComponent]
    public sealed class SDxRobotCameraUiBridge : MonoBehaviour
    {
        public SDxToolsPanel ToolsUI;
        public SDxRobotCameraRig Rig;
        private bool subscribed;
        private bool lastAvailable;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (ToolsUI == null || Rig == null)
            {
                Debug.LogError("[SDx Cameras] Missing Tools UI/rig reference. Run the camera setup menu.", this); return;
            }
            ToolsUI.onCameraViewRequested.RemoveListener(OnCameraRequested);
            ToolsUI.onCameraViewRequested.AddListener(OnCameraRequested);
            SDxScenario1CommandBridge.ScenarioStartAccepted -= OnScenarioAccepted;
            SDxScenario1CommandBridge.ScenarioStartAccepted += OnScenarioAccepted;
            Rig.ViewChanged -= OnViewChanged;
            Rig.ViewChanged += OnViewChanged;
            subscribed = true;
            RefreshUi();
        }

        private void OnDisable()
        {
            if (Rig != null) Rig.SetFreeInputBlocked(true, true);
            if (subscribed)
            {
                if (ToolsUI != null)
                {
                    ToolsUI.onCameraViewRequested.RemoveListener(OnCameraRequested);

                    ToolsUI.SetCameraControlsState(false, Rig == null ? "Front" : Rig.ActiveView.ToString());
                }
                SDxScenario1CommandBridge.ScenarioStartAccepted -= OnScenarioAccepted;
                if (Rig != null) Rig.ViewChanged -= OnViewChanged;
            }
            subscribed = false;
        }

        private void Update()
        {
            if (!Application.isPlaying || Rig == null) return;
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            bool unavailable = !subscribed || ToolsUI == null;
            Rig.SetFreeInputBlocked(unavailable || ToolsUI.BlocksCameraKeyboardInput || SDxExecutionLogWindow.BlocksCameraKeyboardInput,
                unavailable || ToolsUI.BlocksCameraPointerInput(Input.mousePosition) ||
                SDxExecutionLogWindow.BlocksCameraPointerInput(Input.mousePosition));
#else
            Rig.SetFreeInputBlocked(true, true);
#endif
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !subscribed || Rig == null || ToolsUI == null) return;
            bool available = Rig.IsReady && Rig.isActiveAndEnabled;
            if (available != lastAvailable) RefreshUi();
            if (available) Rig.SetViewport(Rig.ReserveToolsPanelSpace && ToolsUI.isActiveAndEnabled
                ? ToolsUI.GetSceneCameraViewport() : new Rect(0, 0, 1, 1));
        }

        private void OnCameraRequested(string view) { if (Rig != null && Rig.IsReady) Rig.SelectView(view); }
        private void OnViewChanged(SDxRobotCameraView view) { RefreshUi(); }
        private void RefreshUi()
        {
            lastAvailable = Rig != null && Rig.IsReady && Rig.isActiveAndEnabled;
            if (ToolsUI != null) ToolsUI.SetCameraControlsState(lastAvailable, Rig == null ? "Front" : Rig.ActiveView.ToString());
        }

        private void OnScenarioAccepted()
        {
            if (Rig != null) Rig.RequestSnap(4);
            // Keeps the selected view. Rejected/expired LLM results never snap.
        }
    }
}
