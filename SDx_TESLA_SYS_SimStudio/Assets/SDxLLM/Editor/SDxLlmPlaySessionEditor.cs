using System.IO;
using UnityEditor;
using UnityEngine;
using SDx.SimStudio.Commands;

[InitializeOnLoad]
public static class SDxLlmPlaySessionEditor
{
    static SDxLlmPlaySessionEditor()
    {
        // Remove-before-add also makes this safe with domain reload disabled.
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        EditorApplication.quitting -= OnEditorQuitting;
        EditorApplication.quitting += OnEditorQuitting;
        EditorApplication.delayCall += ResumeAfterReload;
    }

    static void ResumeAfterReload()
    {
        if (EditorApplication.isPlaying) SDxLlmPlaySession.BeginForPlay();
    }

    static void OnEditorUpdate() { SDxLlmPlaySession.Poll(); }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) SDxLlmPlaySession.BeginForPlay();
        else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            SDxLlmPlaySession.StopForPlay();
    }

    static void BeforeAssemblyReload()
    {
        SDxLlmPlaySession.StopForPlay();
        SDxLlmPlaySession.ReleaseProcessHandles();
    }

    static void OnEditorQuitting()
    {
        SDxLlmPlaySession.StopForPlay();
        SDxLlmPlaySession.ReleaseProcessHandles();
    }

    [MenuItem("Tools/SDx/LLM/Open Play Service Log")]
    public static void OpenLog()
    {
        string path = SDxLlmPlaySession.LogPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = SDxLlmPlaySession.LogDirectory;
        if (File.Exists(path) || Directory.Exists(path)) EditorUtility.RevealInFinder(path);
        else EditorUtility.DisplayDialog("LLM Play log", "아직 Play 로그가 없습니다. Play를 한 번 실행하면 Logs/SDxLLM에 생성됩니다.", "OK");
    }

    [MenuItem("Tools/SDx/LLM/Show Play Service Status")]
    public static void ShowStatus()
    {
        EditorUtility.DisplayDialog("LLM Play service",
            SDxLlmPlaySession.UserMessage + "\n\nState: " + SDxLlmPlaySession.State +
            "\nPlay-owned Gateway: " + SDxLlmPlaySession.GatewayOwned +
            "\nPlay-owned Ollama: " + SDxLlmPlaySession.OllamaOwned +
            "\n\nLog: " + SDxLlmPlaySession.LogPath, "OK");
    }
}
