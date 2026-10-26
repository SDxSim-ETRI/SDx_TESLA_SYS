using System;

namespace SDx.SimStudio.Cameras
{
    /// <summary>
    /// Filters ONLY camera look samples, not Unity's global input state.
    /// No Unity dependency: the state machine can be regression tested directly.
    /// </summary>
    public sealed class SDxFreeLookInputGate
    {
        public const int MinimumSettleFrames = 3;
        public const float SpikeLimitDegrees = 30f;
        private bool active, primed;
        private int startedFrame, lastReadFrame;
        private double startedTime;

        public void Begin(int frame, double now)
        {
            active = true; primed = false;
            startedFrame = frame; startedTime = now; lastReadFrame = frame;
        }

        public void End() { active = false; primed = false; }

        public bool TryRead(int frame, double now, float rawX, float rawY,
            float sensitivity, float settleSeconds, out float yawDelta, out float pitchDelta)
        {
            yawDelta = pitchDelta = 0;
            if (!active || frame <= lastReadFrame) return false;
            lastReadFrame = frame;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < startedTime) return false;
            double settle = Math.Max(.05, Math.Min(.5, settleSeconds));
            if (frame - startedFrame < MinimumSettleFrames || now - startedTime < settle)
                return false;
            // Also drop the first sample after the barrier (delayed OS/input update).
            if (!primed) { primed = true; return false; }
            if (!Finite(rawX) || !Finite(rawY) || !Finite(sensitivity) || sensitivity <= 0)
                return false;
            float x = rawX * sensitivity, y = -rawY * sensitivity;
            if (!Finite(x) || !Finite(y)) return false;
            // Discard an outlier, rather than applying/queueing part of a warp later.
            if ((double)x * x + (double)y * y > SpikeLimitDegrees * SpikeLimitDegrees)
                return false;
            if (Math.Abs(x) < .000001f && Math.Abs(y) < .000001f) return false;
            yawDelta = x; pitchDelta = y;
            return true;
        }

        private static bool Finite(float value)
        { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
