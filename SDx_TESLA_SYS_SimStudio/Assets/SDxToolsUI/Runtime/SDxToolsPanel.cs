using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace SDx.SimStudio.UI
{
    [Serializable] public sealed class SDxRosRequestEvent : UnityEvent<string, string, string> { }
    [Serializable] public sealed class SDxSceneRequestEvent : UnityEvent<string, string> { }
    [Serializable] public sealed class SDxTextEvent : UnityEvent<string> { }

    /// <summary>
    /// Runtime IMGUI overlay for Unity 2022.3. No Canvas, EventSystem, TMP,
    /// ROS installation or MuJoCo assembly reference is required.
    /// This component never changes physics settings or executes user text.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SDx/Tools Panel")]
    public sealed class SDxToolsPanel : MonoBehaviour
    {
        [Header("Appearance")]
        [Tooltip("Width at the 720-pixel reference height.")]
        [Range(230f, 420f)] public float panelWidth = 254f;
        [Range(0.75f, 1.6f)] public float interfaceScale = 1f;
        [Tooltip("Optional font asset. No font files are included in this package.")]
        public Font interfaceFont;
        public bool useInstalledFontFallback = true;
        public Color titleColor = new Color32(33, 107, 155, 255);
        public Color groupColor = new Color32(48, 135, 181, 255);
        public Color panelColor = new Color32(239, 240, 242, 255);

        [Header("Dropdown Options (UI choices, not detected installations)")]
        public string[] rosModels = { "ROS 2 (Humble)", "ROS 1 (Noetic)" };
        public string[] robotModels = { "Dual FR3-Husky" };
        public string[] rosFunctions = { "Teleop" };
        [SerializeField] private int rosModelIndex;
        [SerializeField] private int robotModelIndex;
        [SerializeField] private int rosFunctionIndex;

        [Header("Scene")]
        [Tooltip("Optional initial local folder. The runtime path field is editable.")]
        public string initialGistFolder = "";
        [Tooltip("Single-line scene instructions; also editable at runtime.")]
        public string instructions = "";
        [Tooltip("Editor Play: use the OS dialog. Standalone: built-in folder browser.")]
        public bool useNativeEditorFolderPicker = true;
        [Tooltip("Optional persistence on this computer, outside the scene file.")]
        public bool rememberGistFolder = false;

        [Header("Text Input")]
        [Tooltip("Auto: built-in Korean two-set composition on Linux; system IME on other desktop platforms.")]
        public SDxTextInputBackend textInputBackend = SDxTextInputBackend.Auto;
        [Tooltip("Built-in mode only. Toggle EN/KR with the button or F2 while editing. Keep the OS layout in English.")]
        public bool startInKoreanMode = false;

        [Header("Integration Hooks (not connected to ROS or AI by default)")]
        public SDxRosRequestEvent onConnectRequested = new SDxRosRequestEvent();
        public SDxRosRequestEvent onDisconnectRequested = new SDxRosRequestEvent();
        public SDxRosRequestEvent onRosFunctionRequested = new SDxRosRequestEvent();
        public SDxTextEvent onGistFolderChanged = new SDxTextEvent();
        public SDxSceneRequestEvent onGenerateRequested = new SDxSceneRequestEvent();
        public SDxTextEvent onPromptSubmitted = new SDxTextEvent();
        public bool logRequests = true;

        public const string PackageVersion = "1.2.0";
        public bool IsRosConnected { get { return rosConnection.IsConnected; } }
        public bool IsKoreanMode { get { return koreanMode; } }
        public bool UsesBuiltInKoreanInput
        {
            get
            {
                if (textInputBackend == SDxTextInputBackend.BuiltInDubeolsik) return true;
                if (textInputBackend == SDxTextInputBackend.SystemIme) return false;
                return Application.platform == RuntimePlatform.LinuxEditor || Application.platform == RuntimePlatform.LinuxPlayer;
            }
        }
        public string SelectedGistFolder { get { return gistFolder; } }
        public string SelectedRosModel { get { return Option(rosModels, rosModelIndex); } }
        public string SelectedRobotModel { get { return Option(robotModels, robotModelIndex); } }
        public string SelectedRosFunction { get { return Option(rosFunctions, rosFunctionIndex); } }
        public string LastSubmittedPrompt { get; private set; } = "";
        public bool IsPromptOpen { get { return promptOpen; } }
        public SDxToolsWindowMode WindowMode { get { return window.Mode; } }
        // For future camera/robot controllers that read raw Input in Update.
        // Those controllers must explicitly respect this flag; IMGUI cannot
        // automatically suppress Input.GetKey/GetMouseButton in other scripts.
        public static bool WantsPointerInput { get; private set; }
        public static bool WantsKeyboardInput { get; private set; }

        private const string PromptControl = "SDx.Tools.Prompt";
        private const string InstructionControl = "SDx.Tools.Instructions";
        private const string GistPathControl = "SDx.Tools.GistPath";
        private const string FolderControl = "SDx.Tools.Folder";
        private readonly SDxRosConnectionState rosConnection = new SDxRosConnectionState();
        private readonly SDxKoreanTextField promptInput = new SDxKoreanTextField();
        private readonly SDxKoreanTextField instructionInput = new SDxKoreanTextField();
        private readonly SDxKoreanTextField pathInput = new SDxKoreanTextField();
        private readonly SDxKoreanTextField folderInput = new SDxKoreanTextField();
        private bool koreanMode, pathDraftDirty, previousPathFocus;
        private string lastTextControl = "", restoreTextFocus = "";
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
        private bool ownsImeMode;
        private IMECompositionMode previousImeMode;
#endif
        private readonly SDxToolsWindowState window = new SDxToolsWindowState();
        private readonly SDxFolderBrowser browser = new SDxFolderBrowser();
        private readonly List<Texture2D> styleTextures = new List<Texture2D>();
        private Font fallbackFont;
        private GUIStyle label, header, field, button, blueButton,
                         smallButton, dropdown, popupItem, popupSelected, hint, whiteHint, errorStyle;
        private bool stylesReady;
        private int styleFontSize;
        private Font styleFont;
        private float scale = 1f;
        private Rect panelRect, contentRect, chatRect, popupAnchor, popupRect, folderRect;
        private Vector2 panelScroll, popupScroll, folderScroll;
        private bool rosExpanded = true, sceneExpanded = true, promptExpanded = true;
        private int activeDropdown = -1;
        private int highlightedOption;
        private bool promptOpen, focusPrompt, folderOpen, folderRequestPending;
        private string promptText = "", gistFolder = "", folderPathEdit = "";
        private string toast = "";
        private float toastUntil;
        private int lastImeFrame = -100;
        private int lastScreenWidth, lastScreenHeight;
        private float lastScale;
        private bool hasCapture;

        private string PreferenceKey { get { return "SDx.Tools.GistFolder." + Application.productName; } }
        private float S(float value) { return value * scale; }
        private int F(float value) { return Mathf.Max(9, Mathf.RoundToInt(S(value))); }

        private void Awake()
        {
            rosConnection.Reset();
            koreanMode = startInKoreanMode;
            instructions = SDxKoreanTextField.SingleLine(instructions);
            gistFolder = SDxKoreanTextField.SingleLine(initialGistFolder);
            if (rememberGistFolder && PlayerPrefs.HasKey(PreferenceKey))
                gistFolder = PlayerPrefs.GetString(PreferenceKey, gistFolder);
            // Keep an editable draft even if the initial folder is not currently mounted.
            Log("UI v" + PackageVersion + " loaded. Text input: " +
                (UsesBuiltInKoreanInput ? "built-in two-set Korean (F2 / EN-KR button)." : "system IME."));
        }

        private void OnValidate() { stylesReady = false; }

        private void OnDisable()
        {
            rosConnection.Reset(); CommitAllText(); RestoreImeMode();
            activeDropdown = -1;
            promptOpen = folderOpen = folderRequestPending = false;
            WantsPointerInput = WantsKeyboardInput = false;
            hasCapture = false;
        }

        private void OnDestroy()
        {
            ClearStyles();
            if (fallbackFont != null) Destroy(fallbackFont);
        }

        private void Update()
        {
            // Open the native editor dialog outside OnGUI to avoid interrupting
            // an IMGUI layout/repaint pass with a modal OS window.
            if (folderRequestPending)
            {
                folderRequestPending = false;
                OpenFolderPickerNow();
            }
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            if (isActiveAndEnabled)
            {
                Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                WantsPointerInput = hasCapture || folderOpen || activeDropdown >= 0 ||
                    panelRect.Contains(mouse) || (promptOpen && chatRect.Contains(mouse));
                if (!string.IsNullOrEmpty(Input.compositionString)) lastImeFrame = Time.frameCount;
            }
#endif
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            scale = Mathf.Clamp(Screen.height / 720f * interfaceScale, 0.60f, 3f);
            EnsureStyles();
            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight ||
                !Mathf.Approximately(scale, lastScale))
            {
                activeDropdown = -1; // avoid a stale popup after resizing
                lastScreenWidth = Screen.width;
                lastScreenHeight = Screen.height;
                lastScale = scale;
            }
            panelRect = new Rect(0, 0,
                window.Mode == SDxToolsWindowMode.Maximized ? Screen.width : Mathf.Min(S(panelWidth), Screen.width),
                window.Mode == SDxToolsWindowMode.Minimized ? S(36) : Screen.height);
            chatRect = MakeChatRect();

            int oldDepth = GUI.depth;
            Color oldColor = GUI.color, oldBackground = GUI.backgroundColor, oldContent = GUI.contentColor;
            bool oldEnabled = GUI.enabled;
            GUI.depth = -500;
            GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            GUI.enabled = true;
            Event e = Event.current;
            try
            {
                ProcessPopupKeyboardAndOutsideClick(e);
                if (activeDropdown < 0)
                {
                    ProcessInputLanguageKeys(e);
                    if (!folderOpen) ProcessPromptKeys(e);
                }
                bool modal = folderOpen || activeDropdown >= 0;
                EventType inputType = e.type;
                bool shield = modal && e.type != EventType.Repaint && e.type != EventType.Layout &&
                    e.type != EventType.Used;
                if (shield) e.type = EventType.Ignore;
                try
                {
                    DrawPanel();
                    if (promptOpen) DrawPromptBar();
                }
                finally { if (shield) e.type = inputType; }

                if (activeDropdown >= 0) DrawDropdownPopup();
                if (folderOpen) DrawFolderBrowser();
                DrawToast();

                if (!string.IsNullOrEmpty(restoreTextFocus) && e.type == EventType.Repaint)
                {
                    GUI.FocusControl(restoreTextFocus); restoreTextFocus = "";
                }
                string focused = GUI.GetNameOfFocusedControl();
                bool textFocused = IsTextControl(focused);
                if (textFocused) lastTextControl = focused;
                if (previousPathFocus && focused != GistPathControl && pathDraftDirty)
                    CommitGistPathDraft(false);
                previousPathFocus = focused == GistPathControl;
                UpdateImeMode(textFocused && Application.isFocused);
                WantsKeyboardInput = promptOpen || folderOpen || activeDropdown >= 0 || textFocused;
                bool over = panelRect.Contains(e.mousePosition) ||
                    (promptOpen && chatRect.Contains(e.mousePosition)) || folderOpen || activeDropdown >= 0;
                WantsPointerInput = over || hasCapture;
                if (e.rawType == EventType.MouseDown && over) hasCapture = true;
                if (e.rawType == EventType.MouseUp) hasCapture = false;
                // Stop unused pointer events reaching lower-priority IMGUI tools.
                if (over && (e.type == EventType.MouseDown || e.type == EventType.MouseUp ||
                    e.type == EventType.ScrollWheel || e.type == EventType.MouseDrag)) e.Use();
            }
            finally
            {
                GUI.depth = oldDepth;
                GUI.color = oldColor;
                GUI.backgroundColor = oldBackground;
                GUI.contentColor = oldContent;
                GUI.enabled = oldEnabled;
            }
        }

        private void DrawPanel()
        {
            Fill(panelRect, panelColor);
            Fill(new Rect(panelRect.xMax - 1, 0, 1, panelRect.height), new Color32(126, 135, 141, 255));
            GUI.BeginGroup(panelRect);
            try
            {
                Fill(new Rect(0, 0, panelRect.width, S(36)), titleColor);
                GUI.Label(new Rect(S(10), 0, panelRect.width - S(65), S(36)), "Tools", header);
                Rect minimize = new Rect(panelRect.width - S(46), S(10), S(17), S(16));
                Rect maximize = new Rect(panelRect.width - S(25), S(10), S(17), S(16));
                if (GUI.Button(minimize, new GUIContent("", "Minimize / Restore panel"), smallButton))
                { window.ToggleMinimize(); activeDropdown = -1; GUI.FocusControl(null); }
                if (GUI.Button(maximize, new GUIContent("", "Maximize / Restore panel"), smallButton))
                { window.ToggleMaximize(); activeDropdown = -1; panelScroll = Vector2.zero; GUI.FocusControl(null); }
                Fill(new Rect(minimize.x + S(4), minimize.y + S(10), S(9), S(1)), new Color32(84, 89, 94, 255));
                if (window.Mode == SDxToolsWindowMode.Maximized)
                {
                    Outline(new Rect(maximize.x + S(6), maximize.y + S(3), S(8), S(8)), Color.gray);
                    Fill(new Rect(maximize.x + S(3), maximize.y + S(6), S(8), S(8)), new Color32(228, 230, 232, 255));
                    Outline(new Rect(maximize.x + S(3), maximize.y + S(6), S(8), S(8)), Color.gray);
                }
                else Outline(new Rect(maximize.x + S(4), maximize.y + S(3), S(9), S(10)), Color.gray);
                if (window.Mode == SDxToolsWindowMode.Minimized) return;

                float rosH = S(rosExpanded ? 162 : 26);
                float sceneH = S(sceneExpanded ? 230 : 26);
                float promptH = S(promptExpanded ? 72 : 26);
                float contentHeight = rosH + sceneH + promptH + S(16);
                float bottom = promptOpen && window.Mode == SDxToolsWindowMode.Maximized ? S(96) : S(6);
                contentRect = new Rect(S(6), S(42), panelRect.width - S(12), Mathf.Max(S(40), panelRect.height - S(42) - bottom));
                bool scrollNeeded = contentHeight > contentRect.height;
                float width = Mathf.Max(S(160), contentRect.width - (scrollNeeded ? S(18) : 0));
                panelScroll = GUI.BeginScrollView(contentRect, panelScroll,
                    new Rect(0, 0, width, contentHeight), false, scrollNeeded);
                try
                {
                    float y = 0;
                    DrawRosSection(new Rect(0, y, width, rosH)); y += rosH + S(8);
                    DrawSceneSection(new Rect(0, y, width, sceneH)); y += sceneH + S(8);
                    DrawPromptSection(new Rect(0, y, width, promptH));
                }
                finally { GUI.EndScrollView(); }
            }
            finally { GUI.EndGroup(); }
        }

        private bool Section(Rect r, string title, bool expanded)
        {
            Fill(r, new Color32(242, 242, 242, 255));
            Outline(r, new Color32(178, 181, 184, 255));
            Rect top = new Rect(r.x, r.y, r.width, S(26));
            Fill(top, groupColor);
            if (GUI.Button(top, GUIContent.none, GUIStyle.none))
            { expanded = !expanded; activeDropdown = -1; GUI.FocusControl(null); }
            GUI.Label(new Rect(top.x + S(8), top.y, top.width - S(28), top.height), title, header);
            Chevron(new Rect(top.x + Mathf.Min(S(title.Length * 6.2f + 18), top.width - S(20)), top.y + S(10), S(9), S(6)), expanded, Color.white);
            return expanded;
        }

        private void DrawRosSection(Rect r)
        {
            rosExpanded = Section(r, "ROS Connection", rosExpanded);
            if (!rosExpanded) return;
            float x = r.x + S(8), y = r.y + S(36), width = r.width - S(16);
            DrawDropdownRow(x, y, width, "ROS Model", rosModels, rosModelIndex, 0); y += S(30);
            DrawDropdownRow(x, y, width, "Robot Model", robotModels, robotModelIndex, 1); y += S(30);
            DrawDropdownRow(x, y, width, "ROS Functions", rosFunctions, rosFunctionIndex, 2, S(46));
            bool previousEnabled = GUI.enabled;
            try
            {
                GUI.enabled = previousEnabled && rosConnection.CanExecute(SelectedRosFunction);
                if (GUI.Button(new Rect(x + width - S(42), y, S(42), S(24)),
                    new GUIContent("Run", "Enabled only after a real ROS connection is confirmed."), button))
                    ExecuteSelectedRosFunction();
            }
            finally { GUI.enabled = previousEnabled; }
            y += S(30);
            float half = (width - S(8)) / 2;
            if (GUI.Button(new Rect(x, y, half, S(26)), "Connect", blueButton))
            {
                Log("Connect requested: " + SelectedRosModel + " | " + SelectedRobotModel + " | " + SelectedRosFunction);
                Notify("Connect requested.");
                onConnectRequested.Invoke(SelectedRosModel, SelectedRobotModel, SelectedRosFunction);
            }
            if (GUI.Button(new Rect(x + half + S(8), y, half, S(26)), "Disconnect", button))
            {
                SetRosConnected(false);
                Log("Disconnect requested."); Notify("Disconnect requested.");
                onDisconnectRequested.Invoke(SelectedRosModel, SelectedRobotModel, SelectedRosFunction);
            }
        }

        private void DrawSceneSection(Rect r)
        {
            sceneExpanded = Section(r, "Scene", sceneExpanded);
            if (!sceneExpanded) return;
            float x = r.x + S(8), width = r.width - S(16);
            GUI.Label(new Rect(x, r.y + S(35), width, S(18)), "Gist Folder Path", label);
            if (GUI.Button(new Rect(x, r.y + S(56), width, S(26)),
                folderRequestPending ? "Opening folder picker..." : "Select Gist Folder...", button)) BrowseForGistFolder();

            string edited = DrawSingleLine(new Rect(x, r.y + S(90), width, S(32)),
                GistPathControl, gistFolder, 4096, "Select or type a folder path...", pathInput);
            if (edited != gistFolder) { gistFolder = edited; pathDraftDirty = true; }

            GUI.Label(new Rect(x, r.y + S(130), width - S(44), S(18)), "Instructions", label);
            DrawLanguageButton(new Rect(x + width - S(38), r.y + S(130), S(38), S(18)));
            instructions = DrawSingleLine(new Rect(x, r.y + S(152), width, S(32)),
                InstructionControl, instructions, 8192, "Enter scene instructions...", instructionInput);
            if (GUI.Button(new Rect(x, r.y + S(192), width, S(28)), "Generate", blueButton)) RequestGenerate();
        }

        private string DrawSingleLine(Rect rect, string name, string text, int maxLength,
                                      string placeholder, SDxKoreanTextField input)
        {
            string result = input.Draw(rect, name, text, maxLength, field, UsesBuiltInKoreanInput && koreanMode);
            if (result.Length == 0 && GUI.GetNameOfFocusedControl() != name)
                GUI.Label(new Rect(rect.x + S(6), rect.y, rect.width - S(12), rect.height), placeholder, hint);
            return result;
        }

        private void DrawPromptSection(Rect r)
        {
            promptExpanded = Section(r, "Prompt", promptExpanded);
            if (!promptExpanded) return;
            if (GUI.Button(new Rect(r.x + S(8), r.y + S(36), r.width - S(16), S(26)), "Send Command", button)) OpenPrompt();
        }

        private void DrawDropdownRow(float x, float y, float width, string caption, string[] options, int selected, int id, float rightReserve = 0)
        {
            float labelWidth = S(89);
            GUI.Label(new Rect(x, y, labelWidth, S(24)), caption, label);
            Rect rect = new Rect(x + labelWidth, y, width - labelWidth - rightReserve, S(24));
            if (GUI.Button(rect, new GUIContent(Option(options, selected), "Select " + caption), dropdown))
            {
                activeDropdown = activeDropdown == id ? -1 : id;
                highlightedOption = Mathf.Clamp(selected, 0, Mathf.Max(0, options == null ? 0 : options.Length - 1));
                popupScroll = Vector2.zero;
                GUI.FocusControl(null);
            }
            Chevron(new Rect(rect.xMax - S(13), rect.y + S(9), S(6), S(5)), true, new Color32(60, 66, 72, 255));
            if (activeDropdown == id)
            {
                popupAnchor = new Rect(panelRect.x + contentRect.x + rect.x - panelScroll.x,
                    panelRect.y + contentRect.y + rect.y - panelScroll.y, rect.width, rect.height);
                float h = Mathf.Min(S(150), S(26) * Mathf.Max(1, options == null ? 0 : options.Length) + S(4));
                float py = popupAnchor.yMax + 1;
                if (py + h > Screen.height - S(4)) py = popupAnchor.y - h - 1;
                popupRect = new Rect(popupAnchor.x, Mathf.Max(0, py), popupAnchor.width, h);
            }
        }

        private string[] ActiveOptions()
        {
            if (activeDropdown == 0) return rosModels ?? new string[0];
            if (activeDropdown == 1) return robotModels ?? new string[0];
            return rosFunctions ?? new string[0];
        }

        private void SelectOption(int index)
        {
            string[] options = ActiveOptions();
            if (options.Length > 0)
            {
                index = Mathf.Clamp(index, 0, options.Length - 1);
                if (activeDropdown == 0) rosModelIndex = index;
                else if (activeDropdown == 1) robotModelIndex = index;
                else rosFunctionIndex = index;
            }
            activeDropdown = -1; GUI.FocusControl(null);
        }

        private void ProcessPopupKeyboardAndOutsideClick(Event e)
        {
            if (activeDropdown < 0 || folderOpen) return;
            if (e.type == EventType.MouseDown && !popupRect.Contains(e.mousePosition))
            { activeDropdown = -1; e.Use(); return; }
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Escape) { activeDropdown = -1; e.Use(); }
            else if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.UpArrow)
            {
                highlightedOption = Mathf.Clamp(highlightedOption + (e.keyCode == KeyCode.DownArrow ? 1 : -1),
                    0, Mathf.Max(0, ActiveOptions().Length - 1));
                popupScroll.y = Mathf.Max(0, highlightedOption * S(26) - popupRect.height + S(30));
                e.Use();
            }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            { SelectOption(highlightedOption); e.Use(); }
        }

        private void DrawDropdownPopup()
        {
            string[] options = ActiveOptions();
            Fill(new Rect(popupRect.x + S(2), popupRect.y + S(3), popupRect.width, popupRect.height), new Color(0, 0, 0, .22f));
            Fill(popupRect, Color.white); Outline(popupRect, new Color32(124, 140, 150, 255));
            bool needsScroll = options.Length * S(26) > popupRect.height - S(4);
            Rect viewport = new Rect(popupRect.x + S(2), popupRect.y + S(2), popupRect.width - S(4), popupRect.height - S(4));
            float width = viewport.width - (needsScroll ? S(18) : 0);
            popupScroll = GUI.BeginScrollView(viewport, popupScroll,
                new Rect(0, 0, width, Mathf.Max(S(26), options.Length * S(26))), false, needsScroll);
            int clicked = -1;
            for (int i = 0; i < options.Length; i++)
                if (GUI.Button(new Rect(0, i * S(26), width, S(26)), options[i] ?? "", i == highlightedOption ? popupSelected : popupItem)) clicked = i;
            if (options.Length == 0) GUI.Label(new Rect(S(4), 0, width - S(8), S(26)), "No options", hint);
            GUI.EndScrollView();
            if (clicked >= 0) SelectOption(clicked);
        }

        private Rect MakeChatRect()
        {
            float left = window.Mode == SDxToolsWindowMode.Normal ? panelRect.width + S(12) : S(12);
            if (Screen.width - left < S(300)) left = S(12);
            return new Rect(left, Screen.height - S(88), Mathf.Max(S(100), Screen.width - left - S(12)), S(76));
        }

        public void OpenPrompt()
        {
            activeDropdown = -1; promptOpen = true; focusPrompt = true;
        }

        private void DrawPromptBar()
        {
            Fill(new Rect(chatRect.x + S(2), chatRect.y + S(3), chatRect.width, chatRect.height), new Color(0, 0, 0, .25f));
            Fill(chatRect, panelColor); Outline(chatRect, new Color32(132, 146, 157, 255));
            Fill(new Rect(chatRect.x, chatRect.y, chatRect.width, S(26)), groupColor);
            GUI.Label(new Rect(chatRect.x + S(9), chatRect.y, S(84), S(26)), "Command", header);
            float hintWidth = chatRect.width - S(190);
            if (hintWidth >= S(120))
                GUI.Label(new Rect(chatRect.x + S(98), chatRect.y, hintWidth, S(26)),
                    UsesBuiltInKoreanInput ? "Enter: submit   F2: EN/KR   Esc: cancel" : "Enter: submit   Esc: cancel", whiteHint);
            DrawLanguageButton(new Rect(chatRect.xMax - S(76), chatRect.y + S(4), S(42), S(18)));
            if (GUI.Button(new Rect(chatRect.xMax - S(24), chatRect.y + S(4), S(18), S(18)), "x", smallButton)) ClosePrompt();
            Rect input = new Rect(chatRect.x + S(8), chatRect.y + S(34), chatRect.width - S(16), S(32));
            promptText = DrawSingleLine(input, PromptControl, promptText, 4096, "Type a command...", promptInput);
            if (focusPrompt && Event.current.type == EventType.Repaint)
            { GUI.FocusControl(PromptControl); focusPrompt = false; }
        }

        private void ProcessPromptKeys(Event e)
        {
            if (!promptOpen || e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Escape) { ClosePrompt(); e.Use(); return; }
            if (GUI.GetNameOfFocusedControl() != PromptControl) return;
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            // Do not submit the Return used to confirm an IME composition.
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            if (!UsesBuiltInKoreanInput)
            {
                if (!string.IsNullOrEmpty(Input.compositionString))
                { lastImeFrame = Time.frameCount; return; }
                if (Time.frameCount - lastImeFrame <= 1) return;
            }
#endif
            promptInput.Commit();
            string text = promptText.Trim();
            ClosePrompt();
            promptText = "";
            e.Use();
            if (text.Length == 0) return;
            LastSubmittedPrompt = text;
            Log("Prompt submitted: " + text);
            onPromptSubmitted.Invoke(text);
        }

        private void ClosePrompt()
        {
            promptInput.Commit();
            promptOpen = false; focusPrompt = false; GUI.FocusControl(null);
        }

        public void BrowseForGistFolder()
        {
            activeDropdown = -1;
            folderRequestPending = true;
        }

        private void OpenFolderPickerNow()
        {
            string start = Directory.Exists(gistFolder) ? gistFolder : SDxFolderBrowser.HomeDirectory();
#if UNITY_EDITOR
            if (useNativeEditorFolderPicker)
            {
                try
                {
                    string selected = UnityEditor.EditorUtility.OpenFolderPanel("Select Gist Folder", start, "");
                    if (!string.IsNullOrEmpty(selected)) SetGistFolder(selected);
                    return;
                }
                catch (Exception ex) { Debug.LogWarning("[SDx Tools] Native dialog unavailable; opening built-in picker. " + ex.Message, this); }
            }
#endif
            browser.Navigate(start); folderPathEdit = browser.CurrentDirectory;
            folderScroll = Vector2.zero; folderOpen = true;
        }

        public bool SetGistFolder(string path)
        {
            try
            {
                path = NormalizeFolderDraft(path);
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                { Notify("The selected folder does not exist."); return false; }
                pathInput.Commit();
                gistFolder = Path.GetFullPath(path);
                pathDraftDirty = false;
                if (rememberGistFolder) { PlayerPrefs.SetString(PreferenceKey, gistFolder); PlayerPrefs.Save(); }
                onGistFolderChanged.Invoke(gistFolder);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException ||
                                       ex is NotSupportedException || ex is System.Security.SecurityException)
            { Notify(ex.Message); return false; }
        }

        public void RequestGenerate()
        {
            CommitAllText();
            if (!CommitGistPathDraft(true)) return;
            if (string.IsNullOrWhiteSpace(instructions))
            { Notify("Enter scene instructions first."); return; }
            Log("Generate requested. Folder: " + gistFolder + " | Instructions: " + instructions);
            Notify("Generate requested.");
            onGenerateRequested.Invoke(gistFolder, instructions.Trim());
        }

        private void DrawFolderBrowser()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, .35f));
            float width = Mathf.Min(S(650), Screen.width - S(24));
            float height = Mathf.Min(S(470), Screen.height - S(24));
            folderRect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            Fill(folderRect, panelColor); Outline(folderRect, new Color32(125, 142, 154, 255));
            GUI.BeginGroup(folderRect);
            string navigate = null;
            bool close = false, select = false;
            try
            {
                Fill(new Rect(0, 0, width, S(34)), titleColor);
                GUI.Label(new Rect(S(12), 0, width - S(24), S(34)), "Select Gist Folder", header);
                GUI.Label(new Rect(S(12), S(44), S(48), S(25)), "Folder", label);
                folderPathEdit = DrawSingleLine(new Rect(S(66), S(44), width - S(132), S(25)),
                    FolderControl, folderPathEdit, 4096, "Folder path...", folderInput);
                if (GUI.Button(new Rect(width - S(58), S(44), S(46), S(25)), "Go", button)) navigate = folderPathEdit;
                if (GUI.Button(new Rect(S(12), S(77), S(60), S(25)), "Up", button))
                { browser.Up(); folderPathEdit = browser.CurrentDirectory; folderScroll = Vector2.zero; }
                if (GUI.Button(new Rect(S(80), S(77), S(66), S(25)), "Home", button)) navigate = SDxFolderBrowser.HomeDirectory();
                if (GUI.Button(new Rect(S(154), S(77), S(78), S(25)), "Refresh", button)) navigate = browser.CurrentDirectory;
                Rect list = new Rect(S(12), S(112), width - S(24), Mathf.Max(S(60), height - S(197)));
                Fill(list, Color.white); Outline(list, new Color32(175, 180, 185, 255));
                float listWidth = list.width - S(20);
                folderScroll = GUI.BeginScrollView(list, folderScroll,
                    new Rect(0, 0, listWidth, Mathf.Max(list.height - 1, browser.Directories.Length * S(27))), false, false);
                try
                {
                    if (browser.Directories.Length == 0)
                        GUI.Label(new Rect(S(8), S(4), listWidth - S(16), S(26)), "No subfolders. You can select this folder.", hint);
                    for (int i = 0; i < browser.Directories.Length; i++)
                    {
                        string dir = browser.Directories[i];
                        if (GUI.Button(new Rect(S(3), i * S(27), listWidth - S(6), S(27)),
                            new GUIContent("[+]  " + Path.GetFileName(dir), dir), popupItem)) navigate = dir;
                    }
                }
                finally { GUI.EndScrollView(); }
                GUI.Label(new Rect(S(12), height - S(79), width - S(24), S(31)),
                    string.IsNullOrEmpty(browser.Error) ? "Click a folder to open it, then choose Select Folder." : browser.Error,
                    string.IsNullOrEmpty(browser.Error) ? hint : errorStyle);
                if (GUI.Button(new Rect(width - S(239), height - S(40), S(94), S(28)), "Cancel", button)) close = true;
                if (GUI.Button(new Rect(width - S(137), height - S(40), S(125), S(28)), "Select Folder", blueButton)) select = true;
                Event e = Event.current;
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { close = true; e.Use(); }
                if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) &&
                    GUI.GetNameOfFocusedControl() == FolderControl)
                { navigate = folderPathEdit; e.Use(); }
            }
            finally { GUI.EndGroup(); }
            if (navigate != null && browser.Navigate(navigate))
            { folderPathEdit = browser.CurrentDirectory; folderScroll = Vector2.zero; GUI.FocusControl(null); }
            if (select && SetGistFolder(browser.CurrentDirectory)) close = true;
            if (close) { folderOpen = false; GUI.FocusControl(null); }
        }

        /// <summary>Call on the Unity main thread after a real connection succeeds/fails.</summary>
        public void SetRosConnected(bool connected) { rosConnection.SetConnected(connected); }

        public bool ExecuteSelectedRosFunction()
        {
            if (!rosConnection.CanExecute(SelectedRosFunction)) return false;
            Log("ROS function requested: " + SelectedRosFunction);
            onRosFunctionRequested.Invoke(SelectedRosModel, SelectedRobotModel, SelectedRosFunction);
            return true;
        }

        private static bool IsTextControl(string name)
        {
            return name == PromptControl || name == InstructionControl || name == GistPathControl || name == FolderControl;
        }

        private void CommitAllText()
        {
            promptInput.Commit(); instructionInput.Commit(); pathInput.Commit(); folderInput.Commit();
        }

        private void DrawLanguageButton(Rect rect)
        {
            if (!UsesBuiltInKoreanInput)
            {
                GUI.Label(rect, new GUIContent("IME", "System input method is in use."), hint);
                return;
            }
            if (GUI.Button(rect, new GUIContent(koreanMode ? "KR" : "EN",
                "Built-in Korean two-set input. F2 toggles EN/KR. Keep the OS keyboard in English."),
                koreanMode ? blueButton : smallButton))
                ToggleInputLanguage();
        }

        public void SetKoreanMode(bool enabled)
        {
            CommitAllText(); koreanMode = enabled;
        }

        private void ToggleInputLanguage()
        {
            SetKoreanMode(!koreanMode);
            if (IsTextControl(lastTextControl))
            {
                if (lastTextControl == PromptControl && !promptOpen) restoreTextFocus = InstructionControl;
                else if (lastTextControl == FolderControl && !folderOpen) restoreTextFocus = GistPathControl;
                else restoreTextFocus = lastTextControl;
            }
        }

        private void ProcessInputLanguageKeys(Event e)
        {
            if (e.type != EventType.KeyDown || !IsTextControl(GUI.GetNameOfFocusedControl())) return;
            if (UsesBuiltInKoreanInput && (e.keyCode == KeyCode.F2 ||
                (e.keyCode == KeyCode.Space && e.shift && !e.control && !e.alt && !e.command)))
            { ToggleInputLanguage(); e.Use(); return; }
            if (GUI.GetNameOfFocusedControl() == GistPathControl &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
                if (!UsesBuiltInKoreanInput && (!string.IsNullOrEmpty(Input.compositionString) || Time.frameCount - lastImeFrame <= 1)) return;
#endif
                pathInput.Commit(); CommitGistPathDraft(true); e.Use();
            }
        }

        private static string NormalizeFolderDraft(string path)
        {
            path = (path ?? "").Trim();
            if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
                path = path.Substring(1, path.Length - 2);
            if (path == "~") path = SDxFolderBrowser.HomeDirectory();
            else if (path.StartsWith("~/", StringComparison.Ordinal))
                path = Path.Combine(SDxFolderBrowser.HomeDirectory(), path.Substring(2));
            return path;
        }

        private bool CommitGistPathDraft(bool showError)
        {
            string path = NormalizeFolderDraft(gistFolder);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                if (showError) Notify("Enter or select an existing Gist folder.");
                return false; // Keep invalid text editable, never silently discard it.
            }
            if (!pathDraftDirty && gistFolder == path) return true;
            return SetGistFolder(path);
        }

        private void UpdateImeMode(bool textHasFocus)
        {
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            if (!textHasFocus) { RestoreImeMode(); return; }
            if (!ownsImeMode) { previousImeMode = Input.imeCompositionMode; ownsImeMode = true; }
            Input.imeCompositionMode = UsesBuiltInKoreanInput ? IMECompositionMode.Off : IMECompositionMode.On;
#endif
        }

        private void RestoreImeMode()
        {
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            if (ownsImeMode) { Input.imeCompositionMode = previousImeMode; ownsImeMode = false; }
#endif
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { CommitAllText(); RestoreImeMode(); }
        }

        private void Notify(string message) { toast = message; toastUntil = Time.unscaledTime + 3.5f; }
        private void Log(string message) { if (logRequests) Debug.Log("[SDx Tools] " + message, this); }
        private void DrawToast()
        {
            if (string.IsNullOrEmpty(toast) || Time.unscaledTime > toastUntil) return;
            float w = Mathf.Min(S(400), Screen.width - S(24));
            Rect r = new Rect(Screen.width - w - S(12), S(12), w, S(48));
            Fill(r, new Color32(239, 244, 248, 255)); Outline(r, groupColor);
            GUI.Label(new Rect(r.x + S(9), r.y + S(5), r.width - S(18), r.height - S(10)), toast, hint);
        }

        private static string Option(string[] options, int index)
        {
            return options == null || options.Length == 0 ? "(None)" : options[Mathf.Clamp(index, 0, options.Length - 1)] ?? "";
        }

        private void EnsureStyles()
        {
            Font font = interfaceFont;
            if (font == null && useInstalledFontFallback)
            {
                if (fallbackFont == null)
                {
                    try
                    {
                        fallbackFont = Font.CreateDynamicFontFromOSFont(new[] {
                            "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Nanum Gothic", "UnDotum",
                            "Malgun Gothic", "Apple SD Gothic Neo", "Arial", "DejaVu Sans"
                        }, 12);
                        if (fallbackFont != null) fallbackFont.hideFlags = HideFlags.HideAndDontSave;
                    }
                    catch { /* Use the built-in GUI font if OS font discovery is unavailable. */ }
                }
                font = fallbackFont;
            }
            if (font == null) font = GUI.skin.font;
            if (stylesReady && styleFontSize == F(11) && styleFont == font) return;
            ClearStyles(); styleFontSize = F(11); styleFont = font;
            Color ink = new Color32(58, 64, 70, 255);
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = F(11), alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0), richText = false, wordWrap = false };
            label.normal.textColor = ink;
            header = new GUIStyle(label) { fontSize = F(12), fontStyle = FontStyle.Bold };
            header.normal.textColor = Color.white;
            hint = new GUIStyle(label) { wordWrap = true, alignment = TextAnchor.MiddleLeft, fontSize = F(10) };
            hint.normal.textColor = new Color32(108, 115, 124, 255);
            whiteHint = new GUIStyle(hint) { alignment = TextAnchor.MiddleRight };
            whiteHint.normal.textColor = Color.white;
            errorStyle = new GUIStyle(hint); errorStyle.normal.textColor = new Color32(170, 57, 47, 255);
            button = MakeControl(font, new Color32(223, 224, 226, 255), new Color32(232, 236, 239, 255),
                new Color32(201, 211, 219, 255), ink, TextAnchor.MiddleCenter);
            blueButton = MakeControl(font, groupColor, new Color32(57, 150, 194, 255), titleColor, Color.white, TextAnchor.MiddleCenter);
            smallButton = new GUIStyle(button) { padding = new RectOffset(0, 0, 0, 0), fontSize = F(10) };
            dropdown = MakeControl(font, Color.white, new Color32(248, 251, 253, 255), new Color32(228, 239, 247, 255), ink, TextAnchor.MiddleLeft);
            dropdown.padding.right = Mathf.RoundToInt(S(17));
            field = MakeControl(font, Color.white, Color.white, Color.white, ink, TextAnchor.MiddleLeft);
            field.normal.background = field.hover.background;
            field.focused.background = BorderTexture(Color.white, groupColor);
            field.wordWrap = false;
            popupItem = new GUIStyle(label) { padding = new RectOffset(Mathf.RoundToInt(S(6)), Mathf.RoundToInt(S(5)), 0, 0) };
            popupItem.hover.background = BorderTexture(new Color32(225, 240, 249, 255), new Color32(225, 240, 249, 255));
            popupItem.active.background = popupItem.hover.background;
            popupSelected = new GUIStyle(popupItem); popupSelected.normal.background = BorderTexture(new Color32(211, 233, 246, 255), new Color32(211, 233, 246, 255));
            stylesReady = true;
        }

        private GUIStyle MakeControl(Font font, Color normal, Color hover, Color pressed, Color text, TextAnchor alignment)
        {
            var result = new GUIStyle(GUI.skin.button) { font = font, fontSize = F(11), fontStyle = FontStyle.Normal,
                alignment = alignment, richText = false, wordWrap = false, border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(Mathf.RoundToInt(S(6)), Mathf.RoundToInt(S(6)), Mathf.RoundToInt(S(3)), Mathf.RoundToInt(S(3))),
                margin = new RectOffset(0, 0, 0, 0), fixedWidth = 0, fixedHeight = 0 };
            Color borderColor = new Color32(168, 174, 179, 255);
            result.normal.background = BorderTexture(normal, borderColor);
            result.hover.background = BorderTexture(hover, groupColor);
            result.active.background = BorderTexture(pressed, groupColor);
            result.focused.background = result.hover.background;
            result.normal.textColor = result.hover.textColor = result.active.textColor = result.focused.textColor = text;
            result.onNormal.background = result.normal.background;
            result.onHover.background = result.hover.background;
            result.onActive.background = result.active.background;
            result.onFocused.background = result.focused.background;
            result.onNormal.textColor = result.onHover.textColor = result.onActive.textColor = result.onFocused.textColor = text;
            return result;
        }

        private Texture2D BorderTexture(Color fill, Color border)
        {
            const int n = 5;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                pixels[y * n + x] = (x == 0 || y == 0 || x == n - 1 || y == n - 1) ? border : fill;
            t.SetPixels(pixels); t.Apply(); styleTextures.Add(t); return t;
        }

        private void ClearStyles()
        {
            foreach (Texture2D texture in styleTextures) if (texture != null) Destroy(texture);
            styleTextures.Clear(); stylesReady = false;
        }

        private static void Fill(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color previous = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }

        private static void Outline(Rect r, Color color)
        {
            Fill(new Rect(r.x, r.y, r.width, 1), color);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), color);
            Fill(new Rect(r.x, r.y, 1, r.height), color);
            Fill(new Rect(r.xMax - 1, r.y, 1, r.height), color);
        }

        private static void Chevron(Rect r, bool down, Color color)
        {
            int steps = Mathf.Max(3, Mathf.RoundToInt(down ? r.height : r.width * .6f));
            for (int i = 0; i < steps; i++)
            {
                float f = (float)i / steps;
                if (down) Fill(new Rect(r.x + r.width * f * .5f, r.y + i * r.height / steps,
                    r.width * (1 - f), r.height / steps + .1f), color);
                else Fill(new Rect(r.x + i * r.width / steps, r.y + r.height * f * .5f,
                    r.width / steps + .1f, r.height * (1 - f)), color);
            }
        }
    }
}
