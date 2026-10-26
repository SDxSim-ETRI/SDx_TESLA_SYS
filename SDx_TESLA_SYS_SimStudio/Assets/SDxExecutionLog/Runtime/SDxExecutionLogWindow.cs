using System;
using UnityEngine;
using SDx.SimStudio.UI;

namespace SDx.SimStudio.Logging
{
    // Runtime-only overlay. It does not change a Scene, robot/controller state,
    // or the serialized ShowStatus option on the existing controller.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1500)]
    public sealed class SDxExecutionLogWindow : MonoBehaviour
    {
        static SDxExecutionLogWindow instance;
        static int playSerial;
        int attachedPlaySerial = int.MinValue;
        readonly SDxLogWindowLayoutState window = new SDxLogWindowLayoutState();
        SDxToolsPanel tools;
        float nextReferenceCheck;
        bool pointerCaptured, resizeDragged, followNewest = true;
        Vector2 scroll;
        float scale = 1f;

        SDxLogEntry[] entries = new SDxLogEntry[0];
        long snapshotRevision = -1, heightsRevision = -1, droppedCount;
        GUIContent[] messageContents = new GUIContent[0];
        GUIContent[] timeContents = new GUIContent[0];
        GUIContent[] categoryContents = new GUIContent[0];
        float[] rowTops = new float[1];
        float cachedMessageWidth = -1, contentHeight;
        int cachedFontSize;
        Font cachedFont;
        long anchorSequence;
        float anchorOffset;
        bool haveAnchor;
        string latestStatus = "명령과 실행 기록을 기다리는 중입니다.";
        string hoverHint;

        Font fallbackFont, styleFont;
        int styleFontSize;
        bool stylesReady;
        GUIStyle titleStyle, columnStyle, messageStyle, errorMessageStyle, warningMessageStyle;
        GUIStyle timeStyle, categoryStyle, footerStyle, buttonStyle, tooltipStyle;

        static readonly Color Background = new Color(0.035f, 0.055f, 0.078f, 0.45f);
        static readonly Color Header = new Color(0.045f, 0.19f, 0.29f, 0.60f);
        static readonly Color Divider = new Color(0.57f, 0.70f, 0.80f, 0.18f);
        static readonly Color Ink = new Color32(231, 237, 242, 255);
        static readonly Color MutedInk = new Color32(161, 179, 192, 255);
        static readonly Color ErrorInk = new Color32(255, 145, 139, 255);
        static readonly Color WarningInk = new Color32(242, 202, 126, 255);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            // Exactly one log reset for each Play, including disabled domain
            // reload. Recreating a view or restarting a scenario does not clear it.
            SDxExecutionLog.ResetForPlay();
            SDxExecutionLog.OverlayActive = false;
            instance = null;
            unchecked { ++playSerial; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartBeforeSceneLoad() { EnsureCreated(); }

        // Public for the Editor after script reload / EnteredPlayMode fallback.
        public static void EnsureCreated()
        {
            if (!Application.isPlaying) return;
            if (instance != null && instance.isActiveAndEnabled)
            {
                SDxExecutionLog.OverlayActive = true;
                return;
            }
            // HideAndDontSave objects are not returned by FindObjectsOfType.
            // Resource search is done only at bootstrap, never per row/frame.
            var candidates = Resources.FindObjectsOfTypeAll<SDxExecutionLogWindow>();
            foreach (var candidate in candidates)
            {
                if (!candidate.gameObject.scene.IsValid() || !candidate.gameObject.activeInHierarchy) continue;
                instance = candidate;
                foreach (var other in candidates)
                    if (other != candidate && other.gameObject.scene.IsValid()) other.enabled = false;
                candidate.enabled = true;
                candidate.AttachToPlay();
                return;
            }
            var owner = new GameObject("SDx Execution Log (runtime)");
            owner.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<SDxExecutionLogWindow>();
            instance.AttachToPlay();
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (instance != null && instance != this)
            {
                enabled = false;
                return;
            }
            instance = this;
            AttachToPlay();
        }

        void AttachToPlay()
        {
            if (attachedPlaySerial != playSerial)
            {
                attachedPlaySerial = playSerial;
                window.Restore();
                followNewest = true;
                ReleaseOwnPointer();
                scroll = Vector2.zero;
                snapshotRevision = heightsRevision = -1;
                entries = new SDxLogEntry[0];
                rowTops = new float[1];
                haveAnchor = false;
                latestStatus = "명령과 실행 기록을 기다리는 중입니다.";
            }
            FindTools();
            SDxExecutionLog.OverlayActive = true;
        }

        void Update()
        {
            if (pointerCaptured)
            {
                // Unity IMGUI may miss MouseUp when the user releases outside
                // the Game view. Check the actual physical button every frame.
                if (!Application.isFocused ||
                    (Time.frameCount > captureStartFrame && !IsCapturedButtonHeld()))
                    ReleaseOwnPointer();
            }
            if (tools == null && Time.unscaledTime >= nextReferenceCheck)
            {
                FindTools();
                nextReferenceCheck = Time.unscaledTime + 1f;
            }
        }

        void FindTools()
        {
            if (tools != null && tools.isActiveAndEnabled) return;
            tools = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<SDxToolsPanel>(true))
                if (candidate.isActiveAndEnabled) { tools = candidate; break; }
        }

        public static void ShutdownView()
        {
            if (instance != null)
            {
                instance.ReleaseOwnPointer();
                instance.enabled = false;
            }
            instance = null;
            SDxExecutionLog.OverlayActive = false;
        }

        void OnDisable()
        {
            ReleaseOwnPointer();
            ClearStyles();
            if (fallbackFont != null) { Destroy(fallbackFont); fallbackFont = null; }
            if (instance != this) return;
            instance = null;
            SDxExecutionLog.OverlayActive = false;
        }

        void OnDestroy()
        {
            OnDisable();
            ClearStyles();
            if (fallbackFont != null) Destroy(fallbackFont);
        }

        void OnApplicationFocus(bool focused) { if (!focused) ReleaseOwnPointer(); }
        void OnApplicationQuit() { ShutdownView(); }

        Rect CalculateBounds()
        {
            float viewportLeft = tools != null && tools.isActiveAndEnabled
                ? tools.GetSceneCameraViewport().xMin * Screen.width : 0;
            var bounds = window.Calculate(Screen.width, Screen.height, viewportLeft);
            return new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        // Called from the existing camera bridge before the rig's LateUpdate.
        // Bounds are computed from current Screen/window state, not last OnGUI.
        public static bool BlocksCameraPointerInput(Vector2 bottomLeftScreenPosition)
        {
            var owner = instance;
            return owner != null && owner.isActiveAndEnabled &&
                (owner.pointerCaptured || owner.CalculateBounds().Contains(new Vector2(
                    bottomLeftScreenPosition.x, Screen.height - bottomLeftScreenPosition.y)));
        }

        public static bool BlocksCameraKeyboardInput
        {
            get
            {
                return instance != null && instance.isActiveAndEnabled &&
                    instance.window.Mode == SDxLogWindowMode.Maximized;
            }
        }

        // One passive control ID for this overlay. Keyboard events never enter
        // its input handler; there are no GUI.Button/ScrollView input controls.
        enum PointerTarget { None, Body, Latest, Minimize, Maximize, Scrollbar, ResizeLeft, ResizeBottom, ResizeCorner }
        const int PointerHint = 0x5344584c;
        int ownedControl, capturedButton = -1, captureStartFrame;
        PointerTarget pressedTarget;
        Vector2 dragStartMouse;
        SDxLogWindowBounds dragStartBounds;
        float thumbGrabOffset;
        Rect latestRect, minimizeRect, maximizeRect, bodyRect, clipRect, trackRect, thumbRect;
        float headerHeight, columnHeight, footerHeight, contentWidth, messageWidth, messageX, categoryX;
        float timeWidth, categoryWidth, cellPad, maxScroll;
        bool historyVisible;

        float ViewportLeft()
        {
            return tools != null && tools.isActiveAndEnabled
                ? tools.GetSceneCameraViewport().xMin * Screen.width : 0f;
        }

        bool ToolsOwnsModalInput()
        {
            // A position outside every panel separates modal/capture state
            // from an ordinary pointer hovering over the left Tools panel.
            return tools != null && tools.isActiveAndEnabled &&
                tools.BlocksCameraPointerInput(new Vector2(-1f, -1f));
        }

        void ReleaseOwnPointer()
        {
            // No IMGUI hotControl ownership: the log may never lock the Tools UI.
            ownedControl = 0;
            capturedButton = -1;
            pointerCaptured = false;
            resizeDragged = false;
            pressedTarget = PointerTarget.None;
            hoverHint = null;
        }

        bool IsCapturedButtonHeld()
        {
            if (capturedButton < 0 || capturedButton > 2) return false;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            if (capturedButton == 0) return mouse.leftButton.isPressed;
            if (capturedButton == 1) return mouse.rightButton.isPressed;
            return mouse.middleButton.isPressed;
#else
            return Input.GetMouseButton(capturedButton);
#endif
        }

        static bool IsPointerEvent(EventType type)
        {
            return type == EventType.MouseDown || type == EventType.MouseUp ||
                type == EventType.MouseDrag || type == EventType.ScrollWheel;
        }

        void OnGUI()
        {
            if (!Application.isPlaying || instance != this) return;
            // GUI.depth belongs to this behaviour, not to a shared color scope.
            // Keep the log behind Tools (0/-500), including the Prompt popup.
            GUI.depth = 100;
            int controlId = GUIUtility.GetControlID(PointerHint, FocusType.Passive);
            Event e = Event.current;
            if (e.type == EventType.Ignore)
            {
                ReleaseOwnPointer();
                return;
            }
            // A MouseMove represents an unpressed pointer, even when MouseUp
            // went to the editor rather than this OnGUI component.
            if (pointerCaptured && e.rawType == EventType.MouseMove)
                ReleaseOwnPointer();
            if (pointerCaptured && Time.frameCount > captureStartFrame && !IsCapturedButtonHeld())
                ReleaseOwnPointer();
            if (ToolsOwnsModalInput()) ReleaseOwnPointer();

            // Crucial: no key, text/IME, tab, clipboard, validation or command
            // event is modified, consumed or sent to another IMGUI control.
            bool ownedRelease = pointerCaptured &&
                (e.rawType == EventType.MouseUp || e.rawType == EventType.MouseDrag);
            if (e.type != EventType.Layout && e.type != EventType.Repaint &&
                !IsPointerEvent(e.type) && !ownedRelease) return;

            scale = SDxLogWindowLayoutState.ScaleForHeight(Screen.height);
            EnsureStyles();
            CaptureSnapshot(e.type == EventType.Layout || snapshotRevision < 0);
            Rect bounds = CalculateBounds();
            BuildGeometry(bounds);
            Vector2 globalMouse = e.mousePosition;
            bool toolsShield = tools != null && tools.isActiveAndEnabled &&
                tools.BlocksCameraPointerInput(new Vector2(globalMouse.x, Screen.height - globalMouse.y));
            if (IsPointerEvent(e.type) || ownedRelease)
            {
                HandlePointer(e, controlId, bounds, globalMouse, toolsShield);
                return;
            }
            if (e.type != EventType.Repaint) return;

            Color oldColor = GUI.color, oldBackground = GUI.backgroundColor, oldContent = GUI.contentColor;
            try
            {
                GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
                hoverHint = null;
                // Header/body do not overlap: alpha is not compounded.
                Fill(new Rect(bounds.x, bounds.y + headerHeight, bounds.width,
                    Mathf.Max(0, bounds.height - headerHeight)), Background);
                GUI.BeginClip(bounds);
                try
                {
                    DrawHeader(bounds.width, headerHeight, !toolsShield);
                    if (historyVisible) DrawHistory(bounds.width, bounds.height, headerHeight);
                    DrawResizeGrip(bounds.width, bounds.height, !toolsShield);
                }
                finally { GUI.EndClip(); }
                Outline(bounds, Divider);
                if (!toolsShield && !string.IsNullOrEmpty(hoverHint)) DrawTooltip(globalMouse, hoverHint);
#if UNITY_EDITOR
                if (!toolsShield && window.Mode == SDxLogWindowMode.Normal)
                {
                    UnityEditor.EditorGUIUtility.AddCursorRect(
                        new Rect(bounds.x, bounds.y + headerHeight, S(7), Mathf.Max(0, bounds.height - headerHeight - S(20))),
                        UnityEditor.MouseCursor.ResizeHorizontal);
                    UnityEditor.EditorGUIUtility.AddCursorRect(
                        new Rect(bounds.x + S(20), bounds.yMax - S(7), Mathf.Max(0, bounds.width - S(20)), S(7)),
                        UnityEditor.MouseCursor.ResizeVertical);
                    UnityEditor.EditorGUIUtility.AddCursorRect(
                        new Rect(bounds.x, bounds.yMax - S(20), S(20), S(20)),
                        UnityEditor.MouseCursor.ResizeUpRight);
                }
#endif
            }
            finally
            {
                GUI.color = oldColor;
                GUI.backgroundColor = oldBackground;
                GUI.contentColor = oldContent;
            }
        }

        void BuildGeometry(Rect bounds)
        {
            float width = bounds.width;
            headerHeight = Mathf.Min(S(32), bounds.height);
            float buttonSize = Mathf.Max(0, Mathf.Min(Mathf.Min(S(23), headerHeight - S(6)), (width - S(12)) / 2));
            maximizeRect = new Rect(Mathf.Max(0, width - S(6) - buttonSize), S(3), buttonSize, buttonSize);
            minimizeRect = new Rect(Mathf.Max(0, maximizeRect.x - S(4) - buttonSize), S(3), buttonSize, buttonSize);
            float latestWidth = window.Mode == SDxLogWindowMode.Minimized || width < S(330) ? 0 : S(74);
            latestRect = new Rect(Mathf.Max(0, minimizeRect.x - latestWidth - S(10)), S(3), latestWidth, buttonSize);
            historyVisible = window.Mode != SDxLogWindowMode.Minimized && bounds.height > S(120);
            if (!historyVisible) return;
            columnHeight = S(25); footerHeight = S(49);
            float pad = S(8), barWidth = S(14);
            bodyRect = new Rect(pad, headerHeight + columnHeight,
                Mathf.Max(1, width - 2 * pad), Mathf.Max(1, bounds.height - headerHeight - columnHeight - footerHeight - S(3)));
            contentWidth = Mathf.Max(1, bodyRect.width - barWidth - S(4));
            clipRect = new Rect(bodyRect.x, bodyRect.y, contentWidth, bodyRect.height);
            trackRect = new Rect(bodyRect.xMax - barWidth, bodyRect.y, barWidth, bodyRect.height);
            timeWidth = S(70); categoryWidth = S(48); cellPad = S(6);
            categoryX = cellPad + timeWidth + S(8);
            messageX = categoryX + categoryWidth + S(8);
            messageWidth = Mathf.Max(1, contentWidth - messageX - cellPad);
            EnsureRowHeights(messageWidth);
            maxScroll = Mathf.Max(0, contentHeight - bodyRect.height);
            scroll.x = 0;
            scroll.y = followNewest ? maxScroll : Mathf.Clamp(scroll.y, 0, maxScroll);
            float thumbHeight = maxScroll <= 0 ? trackRect.height :
                Mathf.Clamp(trackRect.height * bodyRect.height / Mathf.Max(1, contentHeight),
                    Mathf.Min(S(28), trackRect.height), trackRect.height);
            float thumbY = trackRect.y + (maxScroll <= 0 ? 0 :
                (trackRect.height - thumbHeight) * scroll.y / maxScroll);
            thumbRect = new Rect(trackRect.x, thumbY, trackRect.width, thumbHeight);
        }

        PointerTarget HitTest(Vector2 local, float width, float height)
        {
            // A clipped GUI does not bound comparisons against mousePosition.
            // Reject points outside the window before testing its controls.
            if (!new Rect(0f, 0f, width, height).Contains(local)) return PointerTarget.None;
            if (minimizeRect.Contains(local)) return PointerTarget.Minimize;
            if (maximizeRect.Contains(local)) return PointerTarget.Maximize;
            if (latestRect.width > 0 && latestRect.Contains(local)) return PointerTarget.Latest;
            if (window.Mode == SDxLogWindowMode.Normal && local.y >= headerHeight)
            {
                if (local.x < S(20) && local.y >= height - S(20)) return PointerTarget.ResizeCorner;
                if (local.x < S(7)) return PointerTarget.ResizeLeft;
                if (local.y >= height - S(7)) return PointerTarget.ResizeBottom;
            }
            if (historyVisible && maxScroll > 0 && trackRect.Contains(local)) return PointerTarget.Scrollbar;
            return PointerTarget.Body;
        }

        void HandlePointer(Event e, int controlId, Rect bounds, Vector2 globalMouse, bool toolsShield)
        {
            Vector2 local = new Vector2(globalMouse.x - bounds.x, globalMouse.y - bounds.y);
            bool over = bounds.Contains(globalMouse);
            if (pointerCaptured)
            {
                if (e.rawType == EventType.MouseUp && (e.button == capturedButton || e.button < 0))
                {
                    PointerTarget action = pressedTarget;
                    bool activate = !toolsShield && over && capturedButton == 0 &&
                        HitTest(local, bounds.width, bounds.height) == action;
                    ReleaseOwnPointer();
                    if (activate)
                    {
                        if (action == PointerTarget.Latest) { followNewest = true; haveAnchor = false; }
                        else if (action == PointerTarget.Minimize) window.ToggleMinimize();
                        else if (action == PointerTarget.Maximize) window.ToggleMaximize();
                    }
                    if (e.type != EventType.Used) e.Use();
                    return;
                }
                if (e.rawType == EventType.MouseDrag && e.button == capturedButton)
                {
                    bool resizeWidth = pressedTarget == PointerTarget.ResizeLeft || pressedTarget == PointerTarget.ResizeCorner;
                    bool resizeHeight = pressedTarget == PointerTarget.ResizeBottom || pressedTarget == PointerTarget.ResizeCorner;
                    if (resizeWidth || resizeHeight)
                    {
                        // A press/hover is not a resize drag. Set this only for
                        // movement after MouseDown on an actual resize handle.
                        if ((globalMouse - dragStartMouse).sqrMagnitude > 0.01f)
                            resizeDragged = true;
                        window.ResizeFrom(dragStartBounds, globalMouse.x - dragStartMouse.x,
                            globalMouse.y - dragStartMouse.y, resizeWidth, resizeHeight,
                            Screen.width, Screen.height, ViewportLeft());
                    }
                    else if (pressedTarget == PointerTarget.Scrollbar)
                    {
                        float travel = Mathf.Max(1, trackRect.height - thumbRect.height);
                        scroll.y = Mathf.Clamp((local.y - trackRect.y - thumbGrabOffset) / travel, 0, 1) * maxScroll;
                    }
                    if (e.type != EventType.Used) e.Use();
                    return;
                }
                // A new press after an interrupted drag must not leave a stale capture.
                if (e.type == EventType.MouseDown) ReleaseOwnPointer();
                else return;
            }
            if (toolsShield || !over || !GUI.enabled || e.type == EventType.Used) return;
            if (e.type == EventType.ScrollWheel)
            {
                if (historyVisible && bodyRect.Contains(local))
                {
                    followNewest = false;
                    scroll.y = Mathf.Clamp(scroll.y + e.delta.y * S(24), 0, maxScroll);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDown)
            {
                // The backdrop is informational: it must never own mouse capture.
                // Capture only a real control or resize handle.
                PointerTarget target = e.button == 0 ? HitTest(local, bounds.width, bounds.height) : PointerTarget.None;
                if (target == PointerTarget.None || target == PointerTarget.Body) return;
                pressedTarget = target;
                resizeDragged = false;
                capturedButton = e.button;
                ownedControl = controlId;
                pointerCaptured = true;
                captureStartFrame = Time.frameCount;
                dragStartMouse = globalMouse;
                dragStartBounds = new SDxLogWindowBounds(bounds.x, bounds.y, bounds.width, bounds.height);
                if (pressedTarget == PointerTarget.Scrollbar)
                {
                    followNewest = false;
                    thumbGrabOffset = thumbRect.Contains(local) ? local.y - thumbRect.y : thumbRect.height / 2;
                    float travel = Mathf.Max(1, trackRect.height - thumbRect.height);
                    scroll.y = Mathf.Clamp((local.y - trackRect.y - thumbGrabOffset) / travel, 0, 1) * maxScroll;
                }
                e.Use();
            }
        }

        void PaintButton(Rect rect, string text, PointerTarget target, bool interactive)
        {
            bool hovered = interactive && rect.Contains(Event.current.mousePosition);
            float alpha = pressedTarget == target && pointerCaptured ? .28f : (hovered ? .18f : .08f);
            Fill(rect, new Color(.60f, .76f, .86f, alpha));
            Outline(rect, new Color(.60f, .76f, .86f, hovered ? .40f : .20f));
            if (!string.IsNullOrEmpty(text)) GUI.Label(rect, text, buttonStyle);
        }

        void DrawHeader(float width, float height, bool interactive)
        {
            Fill(new Rect(0, 0, width, height), Header);
            float titleRight = latestRect.width > 0 ? latestRect.x : minimizeRect.x;
            GUI.Label(new Rect(S(10), 0, Mathf.Max(0, titleRight - S(16)), height),
                "\uC2E4\uD589 \uB85C\uADF8" + (window.Mode == SDxLogWindowMode.Minimized ? "  /  " + entries.Length : ""), titleStyle);
            if (latestRect.width > 0) PaintButton(latestRect, "\uCD5C\uC2E0 \uBCF4\uAE30", PointerTarget.Latest, interactive);
            PaintButton(minimizeRect, "", PointerTarget.Minimize, interactive);
            PaintButton(maximizeRect, "", PointerTarget.Maximize, interactive);
            float buttonSize = minimizeRect.width;
            Fill(new Rect(minimizeRect.x + buttonSize * .25f, minimizeRect.y + buttonSize * .68f,
                buttonSize * .5f, Mathf.Max(1, S(1))), Ink);
            Rect icon = new Rect(maximizeRect.x + buttonSize * .25f, maximizeRect.y + buttonSize * .24f,
                buttonSize * .47f, buttonSize * .47f);
            if (window.Mode == SDxLogWindowMode.Maximized)
                Outline(new Rect(icon.x + S(2), icon.y - S(2), icon.width, icon.height), MutedInk);
            Outline(icon, Ink);
            if (interactive)
            {
                Vector2 mouse = Event.current.mousePosition;
                if (minimizeRect.Contains(mouse)) hoverHint = "\uCD5C\uC18C\uD654 / \uC774\uC804 \uD06C\uAE30\uB85C \uBCF5\uC6D0";
                else if (maximizeRect.Contains(mouse)) hoverHint = "\uCD5C\uB300\uD654 / \uC774\uC804 \uD06C\uAE30\uB85C \uBCF5\uC6D0";
                else if (latestRect.width > 0 && latestRect.Contains(mouse)) hoverHint = "\uCD5C\uC2E0 \uB85C\uADF8\uB85C \uC774\uB3D9";
            }
        }

        void DrawHistory(float width, float height, float header)
        {
            Fill(new Rect(0, header, width, columnHeight), new Color(.24f, .35f, .43f, .08f));
            GUI.Label(new Rect(bodyRect.x + cellPad, header, timeWidth, columnHeight), "\uC2DC\uAC04", columnStyle);
            GUI.Label(new Rect(bodyRect.x + categoryX, header, categoryWidth, columnHeight), "\uAD6C\uBD84", columnStyle);
            GUI.Label(new Rect(bodyRect.x + messageX, header, messageWidth, columnHeight), "\uB0B4\uC6A9", columnStyle);
            Fill(new Rect(bodyRect.x, bodyRect.y - 1, bodyRect.width, 1), Divider);
            // Repaint-only clipping; no ScrollView, slider or button can take focus.
            GUI.BeginClip(clipRect);
            try
            {
                if (entries.Length == 0)
                    GUI.Label(new Rect(cellPad, S(12), Mathf.Max(1, contentWidth - 2 * cellPad), S(48)),
                        "\uC785\uB825\uD55C \uBA85\uB839, LLM \uD310\uC815, \uB85C\uBD07 \uC2E4\uD589 \uB2E8\uACC4\uAC00 \uC2DC\uAC04\uC21C\uC73C\uB85C \uC313\uC785\uB2C8\uB2E4.", messageStyle);
                int first = FirstVisibleRow(scroll.y);
                float limit = scroll.y + bodyRect.height;
                for (int index = first; index < entries.Length && rowTops[index] < limit; ++index)
                {
                    float y = rowTops[index] - scroll.y, rowHeight = rowTops[index + 1] - rowTops[index];
                    if ((index & 1) != 0) Fill(new Rect(0, y, contentWidth, rowHeight), new Color(.52f, .65f, .76f, .025f));
                    if (entries[index].Level == SDxLogLevel.Error)
                        Fill(new Rect(0, y + S(4), S(2), Mathf.Max(1, rowHeight - S(8))), ErrorInk);
                    GUI.Label(new Rect(cellPad, y + S(6), timeWidth, rowHeight - S(12)), timeContents[index], timeStyle);
                    Color prior = GUI.contentColor;
                    GUI.contentColor = CategoryColor(entries[index]);
                    GUI.Label(new Rect(categoryX, y + S(6), categoryWidth, rowHeight - S(12)), categoryContents[index], categoryStyle);
                    GUI.contentColor = prior;
                    GUIStyle rowStyle = entries[index].Level == SDxLogLevel.Error ? errorMessageStyle :
                        (entries[index].Level == SDxLogLevel.Warning ? warningMessageStyle : messageStyle);
                    GUI.Label(new Rect(messageX, y + S(6), messageWidth, rowHeight - S(12)), messageContents[index], rowStyle);
                    Fill(new Rect(0, y + rowHeight - 1, contentWidth, 1), new Color(.5f, .64f, .73f, .06f));
                }
            }
            finally { GUI.EndClip(); }
            Fill(trackRect, new Color(.5f, .65f, .76f, .09f));
            if (maxScroll > 0)
            {
                Fill(thumbRect, new Color(.64f, .77f, .85f, .50f));
                Outline(thumbRect, new Color(.74f, .84f, .92f, .40f));
            }
            float footerY = height - footerHeight;
            Fill(new Rect(bodyRect.x, footerY, bodyRect.width, 1), Divider);
            GUI.Label(new Rect(S(12), footerY + S(5), Mathf.Max(0, width - S(24)), S(20)),
                "\uCD5C\uADFC \uC0C1\uD0DC: " + latestStatus, footerStyle);
            string countText = "\uAE30\uB85D " + entries.Length.ToString("N0") + " / " + SDxExecutionLog.Capacity.ToString("N0") + "\uAC74";
            if (droppedCount > 0) countText += "  /  \uC774\uC804 " + droppedCount.ToString("N0") + "\uAC74 \uC81C\uC678";
            float followWidth = S(112);
            GUI.Label(new Rect(S(28), footerY + S(25), Mathf.Max(0, width - S(40) - followWidth), S(18)), countText, footerStyle);
            GUI.Label(new Rect(Mathf.Max(S(28), width - followWidth - S(12)), footerY + S(25), followWidth, S(18)),
                followNewest ? "\u25CF \uC790\uB3D9 \uB530\uB77C\uAC00\uAE30" : "\uC218\uB3D9 \uD0D0\uC0C9 \uC911", footerStyle);
        }

        void DrawResizeGrip(float width, float height, bool interactive)
        {
            if (window.Mode != SDxLogWindowMode.Normal || !historyVisible) return;
            // Bottom-left diagonal grip: the window remains top-right anchored.
            for (int line = 0; line < 3; ++line)
                for (int dot = 0; dot <= line + 1; ++dot)
                    Fill(new Rect(S(4 + dot * 4), height - S(4 + (line + 1 - dot) * 4), S(2), S(2)), MutedInk);
            // v1.4: no hover rectangle here. The old one-sided x/y test
            // matched the whole lower-left quadrant outside the window.
            // Show the hint only while a real resize drag is still held.
            if (ShouldShowResizeHint(interactive))
                hoverHint = "\uB4DC\uB798\uADF8\uD558\uC5EC \uCC3D \uD06C\uAE30 \uC870\uC808";
        }

        bool ShouldShowResizeHint(bool interactive)
        {
            bool resizing = pressedTarget == PointerTarget.ResizeLeft ||
                pressedTarget == PointerTarget.ResizeBottom || pressedTarget == PointerTarget.ResizeCorner;
            return interactive && GUI.enabled && Application.isFocused &&
                window.Mode == SDxLogWindowMode.Normal && historyVisible &&
                pointerCaptured && capturedButton == 0 && resizeDragged &&
                resizing && IsCapturedButtonHeld();
        }

        void CaptureSnapshot(bool allowed)
        {
            if (!allowed || (snapshotRevision >= 0 && snapshotRevision == SDxExecutionLog.Revision)) return;
            RememberAnchor();
            entries = SDxExecutionLog.Snapshot(out snapshotRevision);
            droppedCount = SDxExecutionLog.DroppedCount;
            messageContents = new GUIContent[entries.Length];
            timeContents = new GUIContent[entries.Length];
            categoryContents = new GUIContent[entries.Length];
            for (int i = 0; i < entries.Length; ++i)
            {
                messageContents[i] = new GUIContent(entries[i].Message ?? "");
                timeContents[i] = new GUIContent(entries[i].Timestamp.ToString("HH:mm:ss"));
                categoryContents[i] = new GUIContent(Category(entries[i]));
                if (entries[i].Source != "Command") latestStatus = entries[i].Message ?? "";
            }
        }

        void RememberAnchor()
        {
            if (followNewest || entries.Length == 0 || rowTops.Length != entries.Length + 1) return;
            int index = FirstVisibleRow(scroll.y);
            if (index >= entries.Length) return;
            anchorSequence = entries[index].Sequence;
            anchorOffset = scroll.y - rowTops[index];
            haveAnchor = true;
        }

        void EnsureRowHeights(float messageWidth)
        {
            if (heightsRevision == snapshotRevision && Mathf.Abs(messageWidth - cachedMessageWidth) < .1f &&
                cachedFont == styleFont && cachedFontSize == styleFontSize) return;
            if (heightsRevision == snapshotRevision) RememberAnchor();
            cachedMessageWidth = messageWidth;
            cachedFont = styleFont;
            cachedFontSize = styleFontSize;
            heightsRevision = snapshotRevision;
            rowTops = new float[entries.Length + 1];
            for (int i = 0; i < entries.Length; ++i)
                rowTops[i + 1] = rowTops[i] + Mathf.Max(S(30),
                    messageStyle.CalcHeight(messageContents[i], messageWidth) + S(12));
            contentHeight = rowTops[entries.Length];
            if (!followNewest && haveAnchor)
            {
                int index = Array.FindIndex(entries, entry => entry.Sequence >= anchorSequence);
                scroll.y = index < 0 ? contentHeight : rowTops[index] +
                    (entries[index].Sequence == anchorSequence ? anchorOffset : 0);
            }
            haveAnchor = false;
        }

        int FirstVisibleRow(float top)
        {
            int low = 0, high = entries.Length;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (rowTops[middle + 1] <= top) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        static string Category(SDxLogEntry entry)
        {
            if (entry.Level == SDxLogLevel.Error) return "오류";
            if (entry.Source == "Command") return "입력";
            if (entry.EventCode == "completed" || entry.EventCode == "placement_warning") return "완료";
            if (entry.Source == "LLM") return "LLM";
            return entry.Source == "Scenario" ? "단계" : "상태";
        }

        static Color CategoryColor(SDxLogEntry entry)
        {
            if (entry.Level == SDxLogLevel.Error) return ErrorInk;
            if (entry.Level == SDxLogLevel.Warning) return WarningInk;
            if (entry.Source == "Command") return new Color32(126, 204, 233, 255);
            if (entry.EventCode == "completed") return new Color32(140, 219, 169, 255);
            if (entry.Source == "LLM") return new Color32(185, 173, 235, 255);
            return entry.Source == "Scenario" ? new Color32(220, 199, 154, 255) : MutedInk;
        }

        void EnsureStyles()
        {
            Font font = tools != null ? tools.interfaceFont : null;
            if (font == null)
            {
                if (fallbackFont == null)
                {
                    try
                    {
                        fallbackFont = Font.CreateDynamicFontFromOSFont(new[] {
                            "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Nanum Gothic", "UnDotum",
                            "Malgun Gothic", "Apple SD Gothic Neo", "Arial", "DejaVu Sans"
                        }, 15);
                        if (fallbackFont != null) fallbackFont.hideFlags = HideFlags.HideAndDontSave;
                    }
                    catch { /* Keep the built-in GUI font if OS discovery fails. */ }
                }
                font = fallbackFont;
            }
            if (font == null) font = GUI.skin.font;
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(S(15)), 12, 21);
            if (stylesReady && styleFont == font && styleFontSize == fontSize) return;
            ClearStyles();
            styleFont = font;
            styleFontSize = fontSize;
            messageStyle = new GUIStyle(GUI.skin.label) {
                font = font, fontSize = fontSize, alignment = TextAnchor.UpperLeft,
                richText = false, wordWrap = true, clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0)
            };
            messageStyle.normal.textColor = Ink;
            errorMessageStyle = new GUIStyle(messageStyle); errorMessageStyle.normal.textColor = ErrorInk;
            warningMessageStyle = new GUIStyle(messageStyle); warningMessageStyle.normal.textColor = WarningInk;
            timeStyle = new GUIStyle(messageStyle) { fontSize = Mathf.Max(11, Mathf.RoundToInt(S(12))), wordWrap = false };
            timeStyle.normal.textColor = MutedInk;
            categoryStyle = new GUIStyle(messageStyle) { fontSize = Mathf.Max(11, Mathf.RoundToInt(S(13))), wordWrap = false };
            categoryStyle.normal.textColor = Color.white;
            columnStyle = new GUIStyle(timeStyle) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            titleStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = false,
                fontSize = Mathf.Max(13, Mathf.RoundToInt(S(17))), fontStyle = FontStyle.Bold };
            footerStyle = new GUIStyle(timeStyle) { alignment = TextAnchor.MiddleLeft };
            buttonStyle = new GUIStyle(GUI.skin.label) {
                font = font, fontSize = Mathf.Max(11, Mathf.RoundToInt(S(12))), richText = false,
                alignment = TextAnchor.MiddleCenter, wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(1, 1, 1, 1)
            };
            buttonStyle.normal.background = null;
            buttonStyle.normal.textColor = Ink;
            tooltipStyle = new GUIStyle(footerStyle) { padding = new RectOffset(6, 6, 4, 4), alignment = TextAnchor.MiddleCenter };
            tooltipStyle.normal.textColor = Ink;
            stylesReady = true;
        }

        void ClearStyles() { stylesReady = false; }

        void DrawTooltip(Vector2 mouse, string text)
        {
            if (Event.current.type != EventType.Repaint) return;
            Vector2 measured = tooltipStyle.CalcSize(new GUIContent(text));
            float width = Mathf.Min(measured.x + S(6), Mathf.Max(1, Screen.width - S(12)));
            float height = Mathf.Max(S(26), measured.y);
            Rect rect = new Rect(Mathf.Clamp(mouse.x - width * .5f, S(6), Mathf.Max(S(6), Screen.width - width - S(6))),
                Mathf.Clamp(mouse.y + S(24), S(6), Mathf.Max(S(6), Screen.height - height - S(6))), width, height);
            Fill(rect, new Color(.035f, .07f, .1f, .97f));
            Outline(rect, Divider);
            GUI.Label(rect, text, tooltipStyle);
        }

        float S(float value) { return value * scale; }

        static void Fill(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void Outline(Rect rect, Color color)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 1), color);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), color);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }
    }
}
