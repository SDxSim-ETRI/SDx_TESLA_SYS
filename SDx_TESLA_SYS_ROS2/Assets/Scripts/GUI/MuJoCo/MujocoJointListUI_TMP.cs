// Assets/Scripts/GUI/MuJoCo/MujocoJointListUI_TMP.cs
//
// 변경 요약 (프리팹 텍스트 스타일 보존):
// - NameText/ValueText/TMP 라벨의 폰트/크기/색/정렬/줄바꿈 등 "텍스트 설정"을 전혀 수정하지 않음
// - 오직 .text(문자열)만 변경
// - 행/컨테이너의 레이아웃(높이/마스크)만 보정하여 표시 문제 방지
// - 버튼 라벨은 TMP/Legacy 모두 지원하되 텍스트만 바꿈
// - 조인트 값 표시는 이전과 동일(리플렉션 + 트랜스폼 근사)
// - unsafe/포인터 미사용, TMP 프리팹: NameText/ValueText 필요

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Mujoco;

public class MujocoJointListUI_TMP : MonoBehaviour
{
    [Header("Hook up in Inspector")]
    public Transform rootOverride;            // humanoid100770 Transform (선택)
    public RectTransform listContainer;       // Scroll View/Viewport/Content/ListContainer
    public GameObject rowPrefab;              // JointRow (자식: NameText, ValueText - TMP)
    public Button dropdownButton;             // Content/DropdownButton (Button)

    [Tooltip("TMP 라벨 (선택). 비워도 자동으로 자식에서 탐색합니다.")]
    public TMP_Text dropdownLabelTMP;         // TMP_Text 라벨

    [Tooltip("Legacy Text 라벨 (선택). 비워도 자동으로 자식에서 탐색합니다.")]
    public Text dropdownLabelUGUI;            // UnityEngine.UI.Text 라벨 (Legacy)

    [Header("Behavior")]
    public bool expandOnStart = true;
    public bool autoRefresh = true;
    public bool showRaw = true;
    public bool showVelocity = false;

    [Header("Layout / Debug")]
    public bool autoFixLayout = true;         // ScrollView & Layout 자동 보정
    public float rowHeight = 32f;             // 고정 행 높이(픽셀)
    [Range(0.1f, 0.9f)] public float nameWidthRatio = 0.45f;
    public bool debugColorizeRows = false;    // 행 배경 칠하기(디버깅용)
    public bool logDebug = true;

    // 내부 상태
    bool _expanded;
    readonly List<Row> _rows = new();

    // 조인트 수집
    private MjBaseJoint[] _joints;
    private string[] _jointNames;
    private JointKind[] _jointKinds;

    private enum JointKind { HingeOrSlide = 1, Ball = 4, Free = 7 }

    class Row
    {
        public GameObject go;
        public RectTransform rt;
        public TMP_Text nameText;
        public TMP_Text valueText;
    }

    // ---------------- Lifecycle ----------------
    void Awake()
    {
        if (dropdownButton != null)
        {
            dropdownButton.onClick.AddListener(ToggleExpand);
            AutoWireDropdownLabel();        // 버튼 자식에서 라벨 자동 탐색
            StyleDropdownButton();          // 버튼 높이만 보정(라벨 스타일은 건드리지 않음)
        }
        else
        {
            LogWarn("dropdownButton 가 연결되어 있지 않습니다. 버튼 없이도 시작 시 펼침 상태라면 목록은 보입니다.");
        }

        if (listContainer == null) { LogError("listContainer 가 연결되어 있지 않습니다."); return; }
        if (rowPrefab == null)     { LogError("rowPrefab 이 연결되어 있지 않습니다."); return; }

        if (autoFixLayout) EnsureScrollViewLayout();

        _expanded = expandOnStart;
        SetListActive(_expanded);
        SetDropdownLabelText(_expanded ? "Hide Joints" : "Show Joints"); // 텍스트만 변경

        if (!CollectJoints())
            LogWarn("조인트를 찾지 못했습니다. rootOverride 지정 또는 씬의 Mj*Joint 존재 확인.");

        if (_expanded) BuildList();
    }

    void Update()
    {
        if (_expanded && autoRefresh) UpdateValues();
    }

    // ---------------- Button / Label ----------------
    void ToggleExpand()
    {
        _expanded = !_expanded;
        SetListActive(_expanded);

        if (_expanded && _rows.Count == 0) BuildList();

        SetDropdownLabelText(_expanded ? "Hide Joints" : "Show Joints"); // 텍스트만 변경

        if (_expanded) UpdateValues();
    }

    void SetDropdownLabelText(string s)
    {
        if (dropdownLabelTMP != null)   dropdownLabelTMP.text = s;
        if (dropdownLabelUGUI != null)  dropdownLabelUGUI.text = s;
    }

    void AutoWireDropdownLabel()
    {
        if (dropdownLabelTMP == null)
            dropdownLabelTMP = dropdownButton.GetComponentInChildren<TMP_Text>(true);
        if (dropdownLabelUGUI == null)
            dropdownLabelUGUI = dropdownButton.GetComponentInChildren<Text>(true);
    }

    void StyleDropdownButton()
    {
        // 버튼 자체의 높이만 보정 (라벨 스타일은 절대 변경하지 않음)
        var brt = dropdownButton.GetComponent<RectTransform>();
        if (brt != null)
        {
            var le = dropdownButton.GetComponent<LayoutElement>() ?? dropdownButton.gameObject.AddComponent<LayoutElement>();
            le.minHeight = Mathf.Max(32f, rowHeight + 4f);
            le.preferredHeight = Mathf.Max(32f, rowHeight + 4f);
        }
    }

    // ---------------- UI ----------------
    void SetListActive(bool on)
    {
        if (listContainer != null)
            listContainer.gameObject.SetActive(on);
    }

    void BuildList()
    {
        ClearList();

        if (_jointNames == null || _jointNames.Length == 0)
        {
            LogWarn("BuildList: 수집된 조인트가 없습니다.");
            return;
        }

        var content = listContainer.parent as RectTransform;

        for (int i = 0; i < _jointNames.Length; i++)
        {
            var rowGO = Instantiate(rowPrefab, listContainer);
            var row   = new Row
            {
                go = rowGO,
                rt = rowGO.GetComponent<RectTransform>()
            };

            // (선택) 디버그용 배경색
            if (debugColorizeRows)
            {
                var img = rowGO.GetComponent<Image>() ?? rowGO.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, (i % 2 == 0) ? 0.05f : 0.10f);
            }

            // Row 크기/앵커만 보정 (텍스트 스타일은 건드리지 않음)
            if (row.rt != null)
            {
                row.rt.anchorMin = new Vector2(0f, 1f);
                row.rt.anchorMax = new Vector2(1f, 1f);
                row.rt.pivot     = new Vector2(0.5f, 1f);
                row.rt.sizeDelta = new Vector2(0f, rowHeight);

                var le = rowGO.GetComponent<LayoutElement>() ?? rowGO.AddComponent<LayoutElement>();
                le.minHeight = rowHeight;
                le.preferredHeight = rowHeight;

                // 프리팹에 HorizontalLayoutGroup이 있어도 자식 높이가 0이 되지 않도록 보정
                var hlg = rowGO.GetComponent<HorizontalLayoutGroup>();
                if (hlg != null)
                {
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandWidth = true;
                    hlg.childForceExpandHeight = true;
                    // spacing/padding은 프리팹 설정을 존중 → 변경하지 않음
                }
            }

            // 자식 TMP 참조
            var nameTf  = rowGO.transform.Find("NameText");
            var valueTf = rowGO.transform.Find("ValueText");
            if (nameTf == null || valueTf == null)
            {
                LogError("JointRow 프리팹에는 자식 'NameText'와 'ValueText' (TMP_Text)가 필요합니다.");
                Destroy(rowGO);
                continue;
            }

            row.nameText  = nameTf.GetComponent<TMP_Text>();
            row.valueText = valueTf.GetComponent<TMP_Text>();
            if (row.nameText == null || row.valueText == null)
            {
                LogError("NameText/ValueText 는 TextMeshProUGUI(TMP_Text)여야 합니다.");
                Destroy(rowGO);
                continue;
            }

            // 좌/우 영역 폭만 anchor로 분할 (텍스트 스타일은 유지)
            var nameRT  = row.nameText.rectTransform;
            var valueRT = row.valueText.rectTransform;

            nameRT.anchorMin = new Vector2(0f, 0f);
            nameRT.anchorMax = new Vector2(nameWidthRatio, 1f);
            nameRT.offsetMin = Vector2.zero; nameRT.offsetMax = Vector2.zero;

            valueRT.anchorMin = new Vector2(nameWidthRatio, 0f);
            valueRT.anchorMax = new Vector2(1f, 1f);
            valueRT.offsetMin = Vector2.zero;
            valueRT.offsetMax = new Vector2(-8f, 0f);

            // 높이 0 방지를 위한 LayoutElement만 추가(스타일 불변)
            var nameLE  = nameRT.GetComponent<LayoutElement>() ?? nameRT.gameObject.AddComponent<LayoutElement>();
            var valueLE = valueRT.GetComponent<LayoutElement>() ?? valueRT.gameObject.AddComponent<LayoutElement>();
            nameLE.minHeight = rowHeight - 2f;  nameLE.preferredHeight = rowHeight - 2f;
            valueLE.minHeight = rowHeight - 2f; valueLE.preferredHeight = rowHeight - 2f;

            // 텍스트 "내용"만 설정 (스타일은 프리팹 그대로)
            row.nameText.text  = _jointNames[i];
            row.valueText.text = "-";
            _rows.Add(row);
        }

        // 레이아웃 즉시 갱신
        LayoutRebuilder.ForceRebuildLayoutImmediate(listContainer);
        if (content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        LayoutRebuilder.ForceRebuildLayoutImmediate(listContainer);

        LogInfo($"BuildList 완료: {_rows.Count} 개 행 생성(텍스트 스타일 미변경)");
    }

    void ClearList()
    {
        foreach (var r in _rows)
            if (r.go) Destroy(r.go);
        _rows.Clear();
    }

    // ---------------- Joint Collect ----------------
    bool CollectJoints()
    {
        if (rootOverride != null)
            _joints = rootOverride.GetComponentsInChildren<MjBaseJoint>(true);
        else
            _joints = FindObjectsOfType<MjBaseJoint>(true);

        if (_joints == null || _joints.Length == 0) return false;

        _jointNames = new string[_joints.Length];
        _jointKinds = new JointKind[_joints.Length];

        for (int i = 0; i < _joints.Length; i++)
        {
            var j = _joints[i];
            _jointNames[i] = j.gameObject.name;

            if (j is MjFreeJoint)      _jointKinds[i] = JointKind.Free;
            else if (j is MjBallJoint) _jointKinds[i] = JointKind.Ball;
            else                       _jointKinds[i] = JointKind.HingeOrSlide;
        }
        LogInfo($"조인트 { _joints.Length }개 탐지");
        return true;
    }

    // ---------------- Values ----------------
    void UpdateValues()
    {
        if (_joints == null || _joints.Length == 0) return;

        int count = Mathf.Min(_rows.Count, _joints.Length);
        for (int i = 0; i < count; i++)
        {
            var j = _joints[i];
            var kind = _jointKinds[i];
            _rows[i].valueText.text = BuildDisplay(j, kind); // 내용만 갱신
        }
    }

    string BuildDisplay(MjBaseJoint j, JointKind kind)
    {
        object conf;
        bool confOk = TryGetMember(j, new[]
        {
            "Configuration","configuration","Config","config",
            "RawConfiguration","rawConfiguration",
            "Qpos","qpos",
            "Orientation","orientation",
            "Rotation","rotation",
            "Quaternion","quaternion",
            "Value","value",
            "Position","position"
        }, out conf);

        var sb = new StringBuilder(128);
        if (confOk) sb.Append(FormatValue(conf, kind));
        else        sb.Append(ApproxByTransform(j.transform, kind));

        if (showRaw && TryGetMember(j, new[] { "RawConfiguration","rawConfiguration","Qpos","qpos" }, out object raw))
            sb.Append(" | raw=").Append(FormatValue(raw, kind));

        if (showVelocity && TryGetMember(j, new[] { "Velocity","velocity","AngularVelocity","angularVelocity" }, out object vel))
            sb.Append(" | vel=").Append(FormatValue(vel, kind));

        return sb.ToString();
    }

    // ---------------- Helpers ----------------
    static bool TryGetMember(object obj, string[] names, out object value)
    {
        value = null;
        var t = obj.GetType();

        foreach (var n in names)
        {
            var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) { try { value = p.GetValue(obj); return true; } catch { } }

            var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { try { value = f.GetValue(obj); return true; } catch { } }

            var m = t.GetMethod(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (m != null) { try { value = m.Invoke(obj, null); return true; } catch { } }

            var gm = t.GetMethod("get_" + n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (gm != null) { try { value = gm.Invoke(obj, null); return true; } catch { } }
        }
        return false;
    }

    static string FormatValue(object v, JointKind kind)
    {
        if (v == null) return "-";

        switch (v)
        {
            case float f:      return f.ToString("F4");
            case double d:     return d.ToString("F4");
            case int i:        return i.ToString();
            case Vector2 v2:   return $"[{v2.x:F4}, {v2.y:F4}]";
            case Vector3 v3:   return $"[{v3.x:F4}, {v3.y:F4}, {v3.z:F4}]";
            case Vector4 v4:   return $"[{v4.x:F4}, {v4.y:F4}, {v4.z:F4}, {v4.w:F4}]";
            case Quaternion q: return $"[{q.x:F4}, {q.y:F4}, {q.z:F4}, {q.w:F4}]";
        }

        if (v is float[] fa)       return FormatArray(fa);
        if (v is double[] da)      return FormatArray(da);
        if (v is Vector3[] v3a)    return FormatArray(v3a);
        if (v is Quaternion[] qa)  return FormatArray(qa);
        return v.ToString();
    }

    static string FormatArray<T>(IEnumerable<T> arr)
    {
        var sb = new StringBuilder(64);
        sb.Append('[');
        bool first = true;
        foreach (var x in arr)
        {
            if (!first) sb.Append(", ");
            first = false;

            switch (x)
            {
                case float f:      sb.Append(f.ToString("F4")); break;
                case double d:     sb.Append(d.ToString("F4")); break;
                case Vector2 v2:   sb.AppendFormat("[{0:F4}, {1:F4}]", v2.x, v2.y); break;
                case Vector3 v3:   sb.AppendFormat("[{0:F4}, {1:F4}, {2:F4}]", v3.x, v3.y, v3.z); break;
                case Vector4 v4:   sb.AppendFormat("[{0:F4}, {1:F4}, {2:F4}, {3:F4}]", v4.x, v4.y, v4.z, v4.w); break;
                case Quaternion q: sb.AppendFormat("[{0:F4}, {1:F4}, {2:F4}, {3:F4}]", q.x, q.y, q.z, q.w); break;
                default:           sb.Append(x?.ToString()); break;
            }
        }
        sb.Append(']');
        return sb.ToString();
    }

    static string ApproxByTransform(Transform t, JointKind kind)
    {
        switch (kind)
        {
            case JointKind.HingeOrSlide:
                float rz = t.localEulerAngles.z;
                float pz = t.localPosition.z;
                return $"approx rotZ={rz:F1}°, posZ={pz:F3}";
            case JointKind.Ball:
            case JointKind.Free:
                var q = t.localRotation;
                return $"approx quat=[{q.x:F3},{q.y:F3},{q.z:F3},{q.w:F3}]";
            default:
                return "-";
        }
    }

    // ---------------- Layout helpers ----------------
    void EnsureScrollViewLayout()
    {
        var content = listContainer.parent as RectTransform;
        if (content != null)
        {
            var vlg = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;

            var csf = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var viewport = content.parent as RectTransform;
            if (viewport != null && viewport.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
        }

        var listVlg = listContainer.GetComponent<VerticalLayoutGroup>() ?? listContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        listVlg.childAlignment = TextAnchor.UpperLeft;
        listVlg.childControlWidth = true;
        listVlg.childControlHeight = false;
        listVlg.childForceExpandWidth = true;
        listVlg.childForceExpandHeight = false;
        listVlg.spacing = 2f;

        if (FindObjectOfType<EventSystem>() == null)
            _ = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    // ---------------- Logs ----------------
    void LogInfo(string msg) { if (logDebug) Debug.Log($"[MujocoJointListUI_TMP] {msg}", this); }
    void LogWarn(string msg) { Debug.LogWarning($"[MujocoJointListUI_TMP] {msg}", this); }
    void LogError(string msg){ Debug.LogError($"[MujocoJointListUI_TMP] {msg}", this); }
}
