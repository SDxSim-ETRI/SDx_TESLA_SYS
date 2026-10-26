using System;

namespace SDx.SimStudio.Logging
{
    public enum SDxLogWindowMode { Normal, Minimized, Maximized }

    // Plain values make layout and window transitions testable without Unity.
    public struct SDxLogWindowBounds
    {
        public readonly float X, Y, Width, Height;
        public SDxLogWindowBounds(float x, float y, float width, float height)
        { X = x; Y = y; Width = width; Height = height; }
        public bool Contains(float x, float y)
        { return x >= X && x < X + Width && y >= Y && y < Y + Height; }
    }

    public sealed class SDxLogWindowLayoutState
    {
        public SDxLogWindowMode Mode { get; private set; }
        SDxLogWindowMode beforeMinimize = SDxLogWindowMode.Normal;
        float normalWidth = 780f, normalHeight = 390f;
        public const float MinimumWidth = 420f, MinimumHeight = 200f;

        // Values are in reference pixels; the same size scales with the Game view.
        public void SetNormalSize(float pixelWidth, float pixelHeight,
            float screenWidth, float screenHeight, float viewportLeft)
        {
            if (Mode != SDxLogWindowMode.Normal) return;
            var area = AvailableArea(screenWidth, screenHeight, viewportLeft);
            float scale = ScaleForHeight(screenHeight);
            float minWidth = Math.Min(MinimumWidth * scale, area.Width);
            float minHeight = Math.Min(MinimumHeight * scale, area.Height);
            normalWidth = Clamp(Finite(pixelWidth, normalWidth * scale), minWidth, area.Width) / scale;
            normalHeight = Clamp(Finite(pixelHeight, normalHeight * scale), minHeight, area.Height) / scale;
        }

        public void ResizeFrom(SDxLogWindowBounds initial, float deltaX, float deltaY,
            bool changeWidth, bool changeHeight, float screenWidth, float screenHeight, float viewportLeft)
        {
            SetNormalSize(initial.Width - (changeWidth ? Finite(deltaX, 0) : 0),
                initial.Height + (changeHeight ? Finite(deltaY, 0) : 0),
                screenWidth, screenHeight, viewportLeft);
        }

        public void ToggleMinimize()
        {
            if (Mode == SDxLogWindowMode.Minimized) Mode = beforeMinimize;
            else { beforeMinimize = Mode; Mode = SDxLogWindowMode.Minimized; }
        }

        public void ToggleMaximize()
        {
            Mode = Mode == SDxLogWindowMode.Maximized
                ? SDxLogWindowMode.Normal : SDxLogWindowMode.Maximized;
        }

        public void Restore()
        {
            Mode = SDxLogWindowMode.Normal;
            beforeMinimize = SDxLogWindowMode.Normal;
            normalWidth = 780f;
            normalHeight = 390f;
        }

        public static float ScaleForHeight(float height)
        { return Clamp(Finite(height, 1080f) / 1080f, 0.8f, 1.35f); }

        public static float HeaderHeight(float height)
        { return 32f * ScaleForHeight(height); }

        // viewportLeft is the live left edge of the scene viewport in pixels.
        // Maximization therefore leaves the existing normal Tools panel usable.
        public SDxLogWindowBounds Calculate(float screenWidth, float screenHeight, float viewportLeft)
        {
            var area = AvailableArea(screenWidth, screenHeight, viewportLeft);
            float scale = ScaleForHeight(screenHeight);
            float desiredWidth = Mode == SDxLogWindowMode.Maximized ? area.Width :
                (Mode == SDxLogWindowMode.Minimized ? 330f : normalWidth) * scale;
            float desiredHeight = Mode == SDxLogWindowMode.Maximized ? area.Height :
                (Mode == SDxLogWindowMode.Minimized ? 32f : normalHeight) * scale;
            float resultWidth = Math.Min(desiredWidth, area.Width);
            float resultHeight = Math.Min(desiredHeight, area.Height);
            return new SDxLogWindowBounds(area.X + area.Width - resultWidth,
                area.Y, resultWidth, resultHeight);
        }

        static SDxLogWindowBounds AvailableArea(float screenWidth, float screenHeight, float viewportLeft)
        {
            float width = Math.Max(1f, Finite(screenWidth, 1f));
            float height = Math.Max(1f, Finite(screenHeight, 1f));
            float scale = ScaleForHeight(height);
            float margin = Math.Min(12f * scale, Math.Min(width, height) * 0.02f);
            margin = Math.Min(margin, Math.Max(0, (Math.Min(width, height) - 1) / 2));
            float left = Clamp(Finite(viewportLeft, 0), 0, Math.Max(0, width - 1)) + margin;
            left = Math.Min(left, Math.Max(margin, width - margin - 1));
            return new SDxLogWindowBounds(left, margin,
                Math.Max(1, width - left - margin), Math.Max(1, height - 2 * margin));
        }

        static float Finite(float value, float fallback)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value; }
        static float Clamp(float value, float minimum, float maximum)
        { return Math.Max(minimum, Math.Min(maximum, value)); }
    }
}
