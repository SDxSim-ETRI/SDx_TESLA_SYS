// Independent of Unity: window controls affect this panel only.
namespace SDx.SimStudio.UI
{
    public enum SDxToolsWindowMode { Normal, Minimized, Maximized }

    public sealed class SDxToolsWindowState
    {
        public SDxToolsWindowMode Mode { get; private set; }
        private SDxToolsWindowMode beforeMinimize = SDxToolsWindowMode.Normal;

        public void ToggleMinimize()
        {
            if (Mode == SDxToolsWindowMode.Minimized)
                Mode = beforeMinimize;
            else
            {
                beforeMinimize = Mode;
                Mode = SDxToolsWindowMode.Minimized;
            }
        }

        public void ToggleMaximize()
        {
            Mode = Mode == SDxToolsWindowMode.Maximized
                ? SDxToolsWindowMode.Normal : SDxToolsWindowMode.Maximized;
        }

        public void Restore() { Mode = SDxToolsWindowMode.Normal; }
    }
}
