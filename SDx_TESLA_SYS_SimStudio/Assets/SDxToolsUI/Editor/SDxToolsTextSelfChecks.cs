#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace SDx.SimStudio.UI.Editor
{
    /// <summary>Runs against the actual C# composer in Unity; not a UI automation test.</summary>
    public static class SDxToolsTextSelfChecks
    {
        [MenuItem("Tools/SDx/Run Text Input Self Checks", false, 21)]
        public static void Run()
        {
            int count = 0;
            Action<bool, string> check = (ok, name) =>
            {
                if (!ok) throw new InvalidOperationException("Check failed: " + name);
                count++;
            };
            try
            {
                string[] leading = { "r", "R", "s", "e", "E", "f", "a", "q", "Q", "t", "T", "d", "w", "W", "c", "z", "x", "v", "g" };
                string[] vowel = { "k", "o", "i", "O", "j", "p", "u", "P", "h", "hk", "ho", "hl", "y", "n", "nj", "np", "nl", "b", "m", "ml", "l" };
                string[] trailing = { "", "r", "R", "rt", "s", "sw", "sg", "e", "f", "fr", "fa", "fq", "ft", "fx", "fv", "fg", "a", "q", "qt", "t", "T", "d", "w", "c", "z", "x", "v", "g" };
                for (int l = 0; l < leading.Length; l++)
                for (int v = 0; v < vowel.Length; v++)
                for (int t = 0; t < trailing.Length; t++)
                {
                    string expected = ((char)(0xAC00 + (l * 21 + v) * 28 + t)).ToString();
                    string keys = leading[l] + vowel[v] + trailing[t];
                    check(Type(keys) == expected, "syllable " + keys + " -> " + expected);
                }
                // Linux IME may send direct Unicode compatibility/conjoining Jamo
                // instead of the ASCII physical keys tested above.
                CheckText(check, "ㅎㅏㄴㄱㅡㄹ", "한글");
                CheckText(check, "ㅊㅐㄱㅇㅡㄹ ㅈㅣㅂㅇㅓ ㅈㅜㅅㅔㅇㅛ", "책을 집어 주세요");
                CheckText(check, "\u1100\u1161", "가");
                check(SDxHangulComposer.RecomposeLooseJamo("ㅎㅏㄴㄱㅡㄹ") == "한글", "recompose loose compatibility Jamo");
                check(SDxHangulComposer.RecomposeLooseJamo("\u1100\u1161\u11A8") == "각", "recompose conjoining Jamo");
                check(SDxHangulComposer.RecomposeLooseJamo("한글 ㅎㅏㄴㄱㅡㄹ") == "한글 한글", "mixed existing Hangul and raw Jamo");
                check(SDxHangulComposer.RecomposeLooseJamo("간ㅏ") == "가나", "split final consonant on next vowel");
                check(SDxHangulComposer.RecomposeLooseJamo("가ㄱㅏ") == "가가", "continue near existing syllable");
                check(SDxHangulComposer.RecomposeLooseJamo("Hello ㅎㅏㄴㄱㅡㄹ!") == "Hello 한글!", "preserve Latin and punctuation");
                check(SDxHangulComposer.RecomposeLooseJamo("안녕하세요") == "안녕하세요", "preserve composed Korean");
                CheckText(check, "gksrmf", "\uD55C\uAE00");
                CheckText(check, "dkssudgktpdy", "\uC548\uB155\uD558\uC138\uC694");
                CheckText(check, "cordmf wlqdj wntpdy", "\uCC45\uC744 \uC9D1\uC5B4 \uC8FC\uC138\uC694");
                CheckText(check, "dhlsWhr", "\uC67C\uCABD");
                CheckText(check, "rkqt", "\uAC12");
                CheckText(check, "rkqtl", "\uAC11\uC2DC");
                CheckText(check, "rkrk", "\uAC00\uAC00");
                CheckText(check, "Rk Ek Qk Tk Wk", "\uAE4C \uB530 \uBE60 \uC2F8 \uC9DC");
                CheckText(check, "rhk rho rhl dnj dnp dnl dml", "\uACFC \uAD18 \uAD34 \uC6CC \uC6E8 \uC704 \uC758");

                var composer = new SDxHangulComposer();
                string text = ""; int cursor = 0, selection = 0;
                foreach (char key in "gks") composer.TryType(ref text, ref cursor, ref selection, key, 4096);
                check(composer.TryBackspace(ref text, ref cursor, ref selection) && text == "\uD558", "backspace final consonant");
                check(composer.TryBackspace(ref text, ref cursor, ref selection) && text == "\u314E", "backspace vowel");
                check(composer.TryBackspace(ref text, ref cursor, ref selection) && text == "", "backspace first consonant");
                check(!composer.TryBackspace(ref text, ref cursor, ref selection), "empty backspace falls through");

                text = "ABC tail"; cursor = selection = 3;
                foreach (char key in "gks") composer.TryType(ref text, ref cursor, ref selection, key, 4096);
                check(text == "ABC\uD55C tail" && cursor == 4, "insert at caret");
                composer.Commit();
                cursor = 3; selection = 4;
                foreach (char key in "rmf") composer.TryType(ref text, ref cursor, ref selection, key, 4096);
                check(text == "ABC\uAE00 tail", "replace selection");
                composer.Commit(); check(!composer.IsComposing, "explicit commit");
                composer = new SDxHangulComposer(); text = ""; cursor = selection = 0;
                foreach (char key in "gks") composer.TryType(ref text, ref cursor, ref selection, key, 1);
                check(text == "\uD55C", "composition at max length");
                composer.TryType(ref text, ref cursor, ref selection, 'r', 1);
                check(text == "\uD55C" && cursor == 1, "length limit without data loss");
                composer.Validate("changed", 0, 0);
                check(!composer.IsComposing, "external edit ends composition");
                check(SDxKoreanTextField.SingleLine("a\r\nb\nc\td") == "a b c d", "single-line conversion");
                check(SDxKoreanTextField.SingleLine("\uD55C\uAE00") == "\uD55C\uAE00", "Unicode preserved");

                var connection = new SDxRosConnectionState();
                check(!connection.CanExecute("Teleop"), "Run disabled initially");
                connection.SetConnected(true); check(connection.CanExecute("Teleop"), "Run enabled by confirmed callback");
                check(!connection.CanExecute(""), "Run requires a function");
                connection.SetConnected(false); check(!connection.CanExecute("Teleop"), "Run disabled on disconnect");
                connection.SetConnected(true); connection.Reset(); check(!connection.IsConnected, "reset disconnected");
                Debug.Log("[SDx Tools v" + SDxToolsPanel.PackageVersion + "] " + count +
                    " text/state checks passed, including all 11,172 modern Hangul syllables and Unicode Jamo.\n" +
                    "Now check focus, caret editing, EN/KR toggle, paste and Enter submission in Game view.");
                EditorUtility.DisplayDialog("SDx Tools", count + " text/state checks passed.\n\n" +
                    "This does not validate rendered text, fonts, OS IME or GUI event delivery. Test them in Game view.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("SDx Tools", ex.Message, "OK");
            }
        }

        private static void CheckText(Action<bool, string> check, string keys, string expected)
        { check(Type(keys) == expected, "text: " + keys); }

        private static string Type(string keys)
        {
            var composer = new SDxHangulComposer(); string text = ""; int cursor = 0, selection = 0;
            foreach (char key in keys)
            {
                if (!composer.TryType(ref text, ref cursor, ref selection, key, 4096))
                {
                    text = text.Insert(cursor, key.ToString()); cursor++; selection = cursor;
                }
            }
            composer.Commit(); return text;
        }
    }
}
#endif
