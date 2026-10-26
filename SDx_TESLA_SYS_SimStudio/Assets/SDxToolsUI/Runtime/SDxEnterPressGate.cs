namespace SDx.SimStudio.UI
{
    /// <summary>One chat action per physical Return press, not per IMGUI/repeat event.</summary>
    public sealed class SDxEnterPressGate
    {
        private bool returnHeld, keypadHeld;
        private int lastPressFrame = -1;

        public bool KeyDown(bool keypad, int frame)
        {
            bool wasHeld = returnHeld || keypadHeld;
            if (keypad) keypadHeld = true; else returnHeld = true;
            if (wasHeld || frame == lastPressFrame) return false;
            lastPressFrame = frame;
            return true;
        }

        public void KeyUp(bool keypad)
        { if (keypad) keypadHeld = false; else returnHeld = false; }

        // Recover if IMGUI consumed KeyUp or Game view temporarily lost focus.
        public void SyncReleased(bool returnIsDown, bool keypadIsDown)
        {
            if (!returnIsDown) returnHeld = false;
            if (!keypadIsDown) keypadHeld = false;
        }

        public void Reset()
        { returnHeld = keypadHeld = false; lastPressFrame = -1; }
    }
}
