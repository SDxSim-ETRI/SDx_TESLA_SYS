#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using SDx.SimStudio.Cameras;

namespace SDx.SimStudio.UI.Editor
{
    public static class SDxInputFixSelfChecks
    {
        [MenuItem("Tools/SDx/Input/Run Camera and Chat Self Checks", false, 30)]
        public static void Run()
        {
            int count = 0;
            Action<bool, string> verify = (ok, label) =>
            {
                if (!ok) throw new InvalidOperationException(label);
                ++count;
            };
            try
            {
                var look = new SDxFreeLookInputGate();
                float yaw, pitch;
                verify(!look.TryRead(0, 0, 10, 10, 2, .12f, out yaw, out pitch), "Inactive input rejected");
                look.Begin(10, 1.0);
                verify(!look.TryRead(10, 1.0, 100, -100, 2, .12f, out yaw, out pitch), "Lock frame rejected");
                verify(!look.TryRead(11, 1.016, 100, -100, 2, .12f, out yaw, out pitch), "Delayed warp rejected");
                verify(!look.TryRead(12, 1.032, 50, -50, 2, .12f, out yaw, out pitch), "Second warp rejected");
                verify(!look.TryRead(13, 1.060, 1, 1, 2, .12f, out yaw, out pitch), "Time barrier respected");
                verify(!look.TryRead(18, 1.140, 100, 100, 2, .12f, out yaw, out pitch), "First stable sample primed only");
                verify(!look.TryRead(19, 1.156, 0, 0, 2, .12f, out yaw, out pitch), "Zero input does not rewrite rotation");
                verify(look.TryRead(20, 1.172, .5f, -.25f, 2, .12f, out yaw, out pitch), "Normal look accepted");
                verify(Math.Abs(yaw - 1f) < .0001f && Math.Abs(pitch - .5f) < .0001f, "Sensitivity and signs retained");
                verify(!look.TryRead(20, 1.172, 1, 1, 2, .12f, out yaw, out pitch), "No duplicate frame");
                verify(!look.TryRead(21, 1.188, 100, 0, 2, .12f, out yaw, out pitch), "Late spike discarded");
                verify(!look.TryRead(22, 1.204, float.NaN, 0, 2, .12f, out yaw, out pitch), "NaN rejected");
                verify(look.TryRead(23, 1.220, -.5f, 0, 2, .12f, out yaw, out pitch), "Recovery after spike");
                look.End();
                verify(!look.TryRead(24, 1.236, 1, 1, 2, .12f, out yaw, out pitch), "Release stops rotation");
                look.Begin(30, 2.0);
                verify(!look.TryRead(31, 2.016, 1, 1, 2, .12f, out yaw, out pitch), "Every press is guarded");

                var enter = new SDxEnterPressGate();
                verify(enter.KeyDown(false, 1), "First Enter opens");
                verify(!enter.KeyDown(false, 1), "Same event frame cannot submit");
                verify(!enter.KeyDown(false, 2), "Held Enter cannot submit");
                enter.KeyUp(false);
                verify(enter.KeyDown(false, 3), "New Enter can submit");
                verify(!enter.KeyDown(false, 4), "Submit repeat cannot reopen");
                enter.KeyUp(false);
                verify(enter.KeyDown(true, 5), "Keypad Enter supported");
                verify(!enter.KeyDown(false, 5), "Two Enter keys cannot double execute");
                enter.SyncReleased(false, false);
                verify(enter.KeyDown(false, 6), "Lost KeyUp can recover");
                enter.Reset();
                verify(enter.KeyDown(false, 7), "Focus loss reset");

                string text = ""; int cursor = 0, selection = 0;
                var composer = new SDxHangulComposer();
                foreach (char c in "gksrmf")
                    verify(composer.TryType(ref text, ref cursor, ref selection, c, 4096), "Hangul key consumed");
                composer.Commit();
                verify(text == "\uD55C\uAE00", "Last Hangul syllable survives commit");

                Quaternion source = Quaternion.Euler(35, 128, 12);
                Quaternion copied = source;
                look.Begin(40, 3.0);
                if (look.TryRead(41, 3.016, 100, 100, 2, .12f, out yaw, out pitch))
                    copied = Quaternion.AngleAxis(yaw, Vector3.up) * source * Quaternion.AngleAxis(pitch, Vector3.right);
                verify(Quaternion.Angle(copied, source) < .0001f, "Lock sample preserves full quaternion/roll");
                Quaternion incremented = Quaternion.AngleAxis(1, Vector3.up) * source * Quaternion.AngleAxis(.5f, Vector3.right);
                verify(Quaternion.Angle(source, incremented) < 1.51f, "Increment is small, not Euler reconstruction");

                Debug.Log("[SDx Input] " + count + " helper checks passed. Test real RMB, Enter, focus, KR and rendering in Game view.");
                EditorUtility.DisplayDialog("Camera and Chat Checks", count + " checks passed.\n\nThis is not a hardware mouse/IME or rendering test.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Camera and Chat Checks", ex.Message, "OK");
            }
        }
    }
}
#endif
