using System;
using System.Collections.Generic;

namespace SDx.SimStudio.UI
{
    /// <summary>
    /// Small, local two-set (Dubeolsik) composer. It is NOT an OS IME.
    /// No native libraries, global keyboard hooks, network, Hanja or dictionaries.
    /// An active syllable is stored directly in the caller's Unicode string.
    /// All offsets are UTF-16 indices, as used by Unity's TextEditor.
    /// </summary>
    public sealed class SDxHangulComposer
    {
        private struct Syllable
        {
            public int L, V, T;
            public Syllable(int l, int v, int t) { L = l; V = v; T = t; }
            public bool Empty { get { return L < 0 && V < 0; } }
        }

        private static readonly char[] Leads = {
            '\u3131','\u3132','\u3134','\u3137','\u3138','\u3139','\u3141',
            '\u3142','\u3143','\u3145','\u3146','\u3147','\u3148','\u3149',
            '\u314A','\u314B','\u314C','\u314D','\u314E'
        };
        private static readonly char[] Tails = {
            '\0','\u3131','\u3132','\u3133','\u3134','\u3135','\u3136','\u3137',
            '\u3139','\u313A','\u313B','\u313C','\u313D','\u313E','\u313F','\u3140',
            '\u3141','\u3142','\u3144','\u3145','\u3146','\u3147','\u3148','\u314A',
            '\u314B','\u314C','\u314D','\u314E'
        };

        private Syllable current = new Syllable(-1, -1, 0);
        private readonly List<Syllable> history = new List<Syllable>();
        private int start = -1;
        private string visible = "";
        public bool IsComposing { get { return start >= 0 && visible.Length > 0; } }

        public void Commit()
        {
            start = -1; visible = ""; current = new Syllable(-1, -1, 0); history.Clear();
        }

        /// <summary>Returns false for non-Dubeolsik input: the normal text field should handle it.</summary>
        public bool TryType(ref string text, ref int cursor, ref int selection, char key, int maxLength)
        {
            int lead, vowel;
            if (!MapKey(key, out lead, out vowel)) { Commit(); return false; }
            text = text ?? "";
            cursor = Clamp(cursor, 0, text.Length); selection = Clamp(selection, 0, text.Length);
            if (!Matches(text, cursor, selection)) Commit();

            Syllable old = current;
            Syllable next;
            string prefix = "";
            bool newSyllable = !IsComposing;
            bool splitFinal = false;

            if (!IsComposing)
                next = new Syllable(lead, vowel, 0);
            else if (lead >= 0)
            {
                int tail = Array.IndexOf(Tails, Leads[lead]);
                if (old.L >= 0 && old.V >= 0 && old.T == 0 && tail > 0)
                    next = new Syllable(old.L, old.V, tail);
                else
                {
                    int combined = old.T > 0 && tail > 0 ? CombineTail(old.T, tail) : -1;
                    if (combined >= 0) next = new Syllable(old.L, old.V, combined);
                    else { prefix = visible; next = new Syllable(lead, -1, 0); newSyllable = true; }
                }
            }
            else if (old.L >= 0 && old.V < 0)
                next = new Syllable(old.L, vowel, 0);
            else if (old.T == 0)
            {
                int combined = CombineVowel(old.V, vowel);
                if (combined >= 0) next = new Syllable(old.L, combined, 0);
                else { prefix = visible; next = new Syllable(-1, vowel, 0); newSyllable = true; }
            }
            else
            {
                int retained, moved;
                SplitTail(old.T, out retained, out moved);
                prefix = Render(new Syllable(old.L, old.V, retained));
                next = new Syllable(Array.IndexOf(Leads, Tails[moved]), vowel, 0);
                newSyllable = splitFinal = true;
            }

            int replaceStart = IsComposing ? start : Math.Min(cursor, selection);
            int removeCount = IsComposing ? visible.Length : Math.Abs(cursor - selection);
            string glyph = Render(next);
            string replacement = prefix + glyph;
            if (text.Length - removeCount + replacement.Length > Math.Max(0, maxLength))
                return true; // Consume the key, without mutating text or composition state.

            text = text.Remove(replaceStart, removeCount).Insert(replaceStart, replacement);
            if (newSyllable)
            {
                history.Clear(); history.Add(new Syllable(-1, -1, 0));
                if (splitFinal) history.Add(new Syllable(next.L, -1, 0));
            }
            else history.Add(old);
            current = next; visible = glyph; start = replaceStart + prefix.Length;
            cursor = selection = start + glyph.Length;
            return true;
        }

        /// <summary>Undo one composing keystroke. Committed text is left to the normal text editor.</summary>
        public bool TryBackspace(ref string text, ref int cursor, ref int selection)
        {
            text = text ?? "";
            if (!Matches(text, cursor, selection) || history.Count == 0) { Commit(); return false; }
            Syllable previous = history[history.Count - 1]; history.RemoveAt(history.Count - 1);
            string replacement = Render(previous);
            text = text.Remove(start, visible.Length).Insert(start, replacement);
            cursor = selection = start + replacement.Length;
            current = previous; visible = replacement;
            if (previous.Empty) Commit();
            return true;
        }

        public void Validate(string text, int cursor, int selection)
        {
            if (IsComposing && !Matches(text ?? "", cursor, selection)) Commit();
        }

        private bool Matches(string text, int cursor, int selection)
        {
            return IsComposing && cursor == selection && cursor == start + visible.Length &&
                start + visible.Length <= text.Length &&
                string.CompareOrdinal(text, start, visible, 0, visible.Length) == 0;
        }

        private static string Render(Syllable s)
        {
            if (s.L >= 0 && s.V >= 0) return ((char)(0xAC00 + (s.L * 21 + s.V) * 28 + s.T)).ToString();
            if (s.L >= 0) return Leads[s.L].ToString();
            if (s.V >= 0) return ((char)(0x314F + s.V)).ToString();
            return "";
        }

        // Some Linux IMEs deliver Unicode Jamo instead of Latin physical keys. 
        // Only mapping ASCII physical keys would leave ㅎㅏㄴㄱㅡㄹ uncomposed.
        private static bool MapKey(char key, out int l, out int v)
        {
            l = v = -1;
            l = Array.IndexOf(Leads, key);
            if (l >= 0) return true;   // U+3131..U+314E compatibility consonants
            if (key >= '\u314F' && key <= '\u3163')
            {
                v = key - '\u314F';
                return true;           // compatibility vowels, including complex vowels
            }
            if (key >= '\u1100' && key <= '\u1112')
            {
                l = key - '\u1100';  // modern conjoining leading Jamo
                return true;
            }
            if (key >= '\u1161' && key <= '\u1175')
            {
                v = key - '\u1161';  // modern conjoining vowel Jamo
                return true;
            }
            // Uppercase affects only the seven shifted keys on a standard two-set layout.
            switch (key)
            {
                case 'R': l = 1; return true; case 'E': l = 4; return true;
                case 'Q': l = 8; return true; case 'T': l = 10; return true;
                case 'W': l = 13; return true; case 'O': v = 3; return true;
                case 'P': v = 7; return true;
            }
            switch (char.ToLowerInvariant(key))
            {
                case 'r': l = 0; break; case 's': l = 2; break; case 'e': l = 3; break;
                case 'f': l = 5; break; case 'a': l = 6; break; case 'q': l = 7; break;
                case 't': l = 9; break; case 'd': l = 11; break; case 'w': l = 12; break;
                case 'c': l = 14; break; case 'z': l = 15; break; case 'x': l = 16; break;
                case 'v': l = 17; break; case 'g': l = 18; break;
                case 'k': v = 0; break; case 'o': v = 1; break; case 'i': v = 2; break;
                case 'j': v = 4; break; case 'p': v = 5; break; case 'u': v = 6; break;
                case 'h': v = 8; break; case 'y': v = 12; break; case 'n': v = 13; break;
                case 'b': v = 17; break; case 'm': v = 18; break; case 'l': v = 20; break;
                default: return false;
            }
            return true;
        }

        /// <summary>
        /// Recover text if Unity's Linux text widget inserted *raw* Hangul Jamo
        /// without delivering a usable KeyDown character to TryType.
        /// Operates ONLY when loose Jamo are present. English text is unchanged.
        /// This is a fallback, not a second OS IME; it never accesses the keyboard.
        /// </summary>
        public static bool ContainsLooseJamo(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char ch in value)
                if (ch >= '\u3131' && ch <= '\u3163' ||
                    ch >= '\u1100' && ch <= '\u1112' ||
                    ch >= '\u1161' && ch <= '\u1175' ||
                    ch >= '\u11A8' && ch <= '\u11C2') return true;
            return false;
        }

        /// <summary>
        /// Recompose syllables and isolated Unicode Jamo in a mixed text field.
        /// Safe for already composed Hangul, Latin, spaces, emoji and punctuation.
        /// Does not translate ASCII keys: that path is handled by TryType.
        /// </summary>
        public static string RecomposeLooseJamo(string input)
        {
            input = input ?? "";
            if (!ContainsLooseJamo(input)) return input;
            var composer = new SDxHangulComposer();
            string output = "";
            int cursor = 0, selection = 0;
            foreach (char ch in input)
            {
                // Expand an existing Hangul syllable so the next loose Jamo can
                // split its final consonant, e.g. 간 + ㅏ -> 가나.
                if (ch >= '\uAC00' && ch <= '\uD7A3')
                {
                    int id = ch - '\uAC00';
                    int lead = id / (21 * 28);
                    int vowel = (id / 28) % 21;
                    int tail = id % 28;
                    composer.TryType(ref output, ref cursor, ref selection, Leads[lead], int.MaxValue);
                    composer.TryType(ref output, ref cursor, ref selection, (char)('\u314F' + vowel), int.MaxValue);
                    if (tail > 0) AddTail(ref composer, ref output, ref cursor, ref selection, tail);
                    continue;
                }

                int l, v;
                if (ch > 127 && MapKey(ch, out l, out v) &&
                    composer.TryType(ref output, ref cursor, ref selection, ch, int.MaxValue))
                    continue;

                if (ch >= '\u11A8' && ch <= '\u11C2')
                {
                    AddTail(ref composer, ref output, ref cursor, ref selection, ch - '\u11A7');
                    continue;
                }

                composer.Commit();
                output = output.Insert(cursor, ch.ToString());
                cursor++;
                selection = cursor;
            }
            composer.Commit();
            return output;
        }

        private static void AddTail(ref SDxHangulComposer composer, ref string text,
                                    ref int cursor, ref int selection, int tail)
        {
            if (tail <= 0 || tail >= Tails.Length) return;
            int retained, moved;
            SplitTail(tail, out retained, out moved);
            if (retained > 0)
                composer.TryType(ref text, ref cursor, ref selection, Tails[retained], int.MaxValue);
            composer.TryType(ref text, ref cursor, ref selection, Tails[moved], int.MaxValue);
        }

        private static int CombineVowel(int a, int b)
        {
            if (a == 8) { if (b == 0) return 9; if (b == 1) return 10; if (b == 20) return 11; }
            if (a == 13) { if (b == 4) return 14; if (b == 5) return 15; if (b == 20) return 16; }
            if (a == 18 && b == 20) return 19;
            if (a == 9 && b == 20) return 10;
            if (a == 14 && b == 20) return 15;
            return -1;
        }

        private static int CombineTail(int a, int b)
        {
            if (a == 1 && b == 19) return 3;
            if (a == 4) { if (b == 22) return 5; if (b == 27) return 6; }
            if (a == 8)
            {
                if (b == 1) return 9; if (b == 16) return 10; if (b == 17) return 11;
                if (b == 19) return 12; if (b == 25) return 13; if (b == 26) return 14;
                if (b == 27) return 15;
            }
            if (a == 17 && b == 19) return 18;
            return -1;
        }

        private static void SplitTail(int t, out int retained, out int moved)
        {
            retained = 0; moved = t;
            switch (t)
            {
                case 3: retained = 1; moved = 19; break;
                case 5: retained = 4; moved = 22; break;
                case 6: retained = 4; moved = 27; break;
                case 9: retained = 8; moved = 1; break;
                case 10: retained = 8; moved = 16; break;
                case 11: retained = 8; moved = 17; break;
                case 12: retained = 8; moved = 19; break;
                case 13: retained = 8; moved = 25; break;
                case 14: retained = 8; moved = 26; break;
                case 15: retained = 8; moved = 27; break;
                case 18: retained = 17; moved = 19; break;
            }
        }

        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(value, max)); }
    }
}
