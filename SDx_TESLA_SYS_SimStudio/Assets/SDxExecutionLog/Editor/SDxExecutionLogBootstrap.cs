using UnityEditor;
using SDx.SimStudio.Logging;

[InitializeOnLoad]
public static class SDxExecutionLogBootstrap
{
    static SDxExecutionLogBootstrap()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        EditorApplication.quitting -= OnEditorQuitting;
        EditorApplication.quitting += OnEditorQuitting;
        EditorApplication.delayCall += ResumeAfterReload;
    }

    static void ResumeAfterReload()
    {
        if (EditorApplication.isPlaying) SDxExecutionLogWindow.EnsureCreated();
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) SDxExecutionLogWindow.EnsureCreated();
        else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            SDxExecutionLogWindow.ShutdownView();
    }

    static void BeforeAssemblyReload() { SDxExecutionLogWindow.ShutdownView(); }
    static void OnEditorQuitting() { SDxExecutionLogWindow.ShutdownView(); }
}
