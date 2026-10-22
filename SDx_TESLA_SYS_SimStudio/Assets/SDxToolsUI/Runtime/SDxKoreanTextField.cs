using UnityEngine;

namespace SDx.SimStudio.UI
{
    public enum SDxTextInputBackend
    {
        Auto = 0,              // Built-in Dubeolsik on Linux; system IME on other desktop platforms.
        BuiltInDubeolsik = 1,
        SystemIme = 2
    }

    /// <summary>One persistent wrapper per field; retains normal caret, selection and clipboard behavior.</summary>
    public sealed class SDxKoreanTextField
    {
        private readonly SDxHangulComposer composer = new SDxHangulComposer();
        public void Commit() { composer.Commit(); }

        public string Draw(Rect rect, string controlName, string value, int maxLength,
                           GUIStyle style, bool builtInKoreanActive)
        {
            value = SingleLine(value);
            Event e = Event.current;
            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            if (!focused || !builtInKoreanActive) composer.Commit();

            // Commit BEFORE native caret movement, selection or clipboard operations.
            if (e.type == EventType.MouseDown || e.type == EventType.ValidateCommand ||
                e.type == EventType.ExecuteCommand) composer.Commit();

            if (focused && builtInKoreanActive && e.type == EventType.KeyDown)
            {
                if (e.control || e.command || e.alt) composer.Commit();
                else
                {
                    int id = GUIUtility.keyboardControl;
                    var editor = GUIUtility.GetStateObject(typeof(TextEditor), id) as TextEditor;
                    if (editor != null && id != 0)
                    {
                        int cursor = Mathf.Clamp(editor.cursorIndex, 0, value.Length);
                        int selection = Mathf.Clamp(editor.selectIndex, 0, value.Length);
                        bool consumed = false;
                        if (e.keyCode == KeyCode.Backspace)
                            consumed = composer.TryBackspace(ref value, ref cursor, ref selection);
                        else if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow ||
                                 e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow ||
                                 e.keyCode == KeyCode.Home || e.keyCode == KeyCode.End ||
                                 e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Tab ||
                                 e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                                 e.keyCode == KeyCode.Escape) composer.Commit();
                        else if (e.character != '\0')
                        {
                            // Use text events, not a second Input.GetKey loop: avoid duplicate keystrokes.
                            char key = e.shift ? e.character : char.ToLowerInvariant(e.character);
                            consumed = composer.TryType(ref value, ref cursor, ref selection, key, maxLength);
                        }
                        if (consumed)
                        {
                            editor.text = value;
                            editor.cursorIndex = cursor; editor.selectIndex = selection;
                            GUI.changed = true; e.Use();
                        }
                    }
                }
            }

            GUI.SetNextControlName(controlName);
            string result = GUI.TextField(rect, value, maxLength, style);
            result = SingleLine(result);

            // Linux IME can bypass our KeyDown handler and inject separated
            // compatibility/modern Jamo directly into GUI.TextField. Repair
            // them AFTER the widget has handled the event, preserving its caret.
            if (builtInKoreanActive && SDxHangulComposer.ContainsLooseJamo(result))
            {
                string recomposed = SDxHangulComposer.RecomposeLooseJamo(result);
                if (recomposed != result)
                {
                    int id = GUIUtility.keyboardControl;
                    var editor = id != 0
                        ? GUIUtility.GetStateObject(typeof(TextEditor), id) as TextEditor
                        : null;
                    if (editor != null && GUI.GetNameOfFocusedControl() == controlName)
                    {
                        int oldCaret = Mathf.Clamp(editor.cursorIndex, 0, result.Length);
                        int oldSelection = Mathf.Clamp(editor.selectIndex, 0, result.Length);
                        int newCaret = SDxHangulComposer.RecomposeLooseJamo(result.Substring(0, oldCaret)).Length;
                        int newSelection = SDxHangulComposer.RecomposeLooseJamo(result.Substring(0, oldSelection)).Length;
                        editor.text = recomposed;
                        editor.cursorIndex = Mathf.Clamp(newCaret, 0, recomposed.Length);
                        editor.selectIndex = Mathf.Clamp(newSelection, 0, recomposed.Length);
                    }
                    result = recomposed;
                    composer.Commit(); // the fallback has rebuilt the latest state
                    GUI.changed = true;
                }
            }
            if (result != value) composer.Commit(); // native edit/paste committed a new string
            if (GUI.GetNameOfFocusedControl() == controlName && GUIUtility.keyboardControl != 0)
            {
                var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
                if (editor != null)
                {
                    if (editor.text != result) editor.text = result;
                    composer.Validate(result, editor.cursorIndex, editor.selectIndex);
                }
            }
            return result;
        }

        public static string SingleLine(string value)
        {
            return (value ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }
    }
}
