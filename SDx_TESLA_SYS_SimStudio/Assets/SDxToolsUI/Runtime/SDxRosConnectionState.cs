namespace SDx.SimStudio.UI
{
    /// <summary>UI gate only. Connecting must be confirmed by a real integration callback.</summary>
    public sealed class SDxRosConnectionState
    {
        public bool IsConnected { get; private set; }
        public void SetConnected(bool connected) { IsConnected = connected; }
        public void Reset() { IsConnected = false; }
        public bool CanExecute(string function) { return IsConnected && !string.IsNullOrWhiteSpace(function); }
    }
}
