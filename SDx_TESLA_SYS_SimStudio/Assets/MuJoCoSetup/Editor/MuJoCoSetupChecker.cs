// Editor-only project utility. Does not reference the MuJoCo C# assembly,
// so it can still compile while the Git package is being resolved.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

internal static class MuJoCoSetupChecker {
  private const string RequiredVersion = "3.16.0";
  private const string PackageName = "org.mujoco";
  private const string LibraryRelativePath = "Plugins/x86_64/libmujoco.so";

  [DllImport("mujoco", CallingConvention = CallingConvention.Cdecl,
             EntryPoint = "mj_versionString")]
  private static extern IntPtr GetNativeVersionString();

  [MenuItem("Tools/MuJoCo/Check Package and Native Library")]
  private static void CheckInstallation() {
    UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageName);
    bool packageReady = package != null;
    string packageVersion = packageReady ? package.version : "not resolved";
    bool packageMatches = packageReady && package.version == RequiredVersion;

    string libraryPath = Path.Combine(Application.dataPath, LibraryRelativePath);
    bool libraryPresent = File.Exists(libraryPath);

    string nativeStatus;
    bool nativeMatches = false;
    if (Application.platform != RuntimePlatform.LinuxEditor) {
      nativeStatus = "This preflight is for the Ubuntu/Linux x86_64 Editor only.";
    } else if (!libraryPresent) {
      nativeStatus = "MISSING: " + libraryPath;
    } else {
      try {
        IntPtr pointer = GetNativeVersionString();
        string version = Marshal.PtrToStringAnsi(pointer);
        nativeMatches = string.Equals(version, RequiredVersion, StringComparison.Ordinal);
        nativeStatus = "Loaded MuJoCo native version: " + version;
      } catch (Exception ex) {
        nativeStatus = "Native load failed: " + ex.GetType().Name + ": " + ex.Message;
      }
    }

    string message =
      "Target MuJoCo: " + RequiredVersion + "\n" +
      "Unity package: " + (packageReady ? packageVersion : "NOT INSTALLED") + "\n" +
      "Native library: " + nativeStatus + "\n\n" +
      "For installation run setup_mujoco_linux.sh before importing MJCF. " +
      "Restart Unity after installing or replacing a loaded native library.";
    Debug.Log("[MuJoCo Setup] " + message);
    bool allGood = packageMatches && nativeMatches;
    EditorUtility.DisplayDialog(allGood ? "MuJoCo setup OK" : "MuJoCo setup needs attention", message, "OK");
  }

  [MenuItem("Tools/MuJoCo/Copy Existing Linux libmujoco.so...")]
  private static void InstallFromLocalFile() {
    if (Application.platform != RuntimePlatform.LinuxEditor) {
      EditorUtility.DisplayDialog("Linux only", "Select and install a native DLL appropriate for your current platform.", "OK");
      return;
    }
    string sourcePath = EditorUtility.OpenFilePanel(
      "Select the MuJoCo 3.16.0 Linux shared library", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "");
    if (string.IsNullOrEmpty(sourcePath)) return;

    string destinationPath = Path.Combine(Application.dataPath, LibraryRelativePath);
    try {
      if (Path.GetFullPath(sourcePath) == Path.GetFullPath(destinationPath)) {
        EditorUtility.DisplayDialog("Already installed", "The chosen file is already inside this Unity project.", "OK");
        return;
      }
      Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
      if (File.Exists(destinationPath) &&
          !EditorUtility.DisplayDialog("Replace library?", "Overwrite the existing Linux libmujoco.so?", "Replace", "Cancel")) return;
      File.Copy(sourcePath, destinationPath, true);
      AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
      EditorUtility.DisplayDialog("Copied", "Native library copied to Assets/Plugins/x86_64/libmujoco.so. " +
        "Make sure it is MuJoCo 3.16.0 and restart Unity before running the simulation.", "OK");
    } catch (Exception ex) {
      Debug.LogException(ex);
      EditorUtility.DisplayDialog("Copy failed", ex.Message, "OK");
    }
  }
}
#endif
