#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SDx.SimStudio.UI.Editor
{
    public static class SDxToolsPanelInstaller
    {
        [MenuItem("Tools/SDx/Create Tools UI", false, 1)]
        public static void CreateToolsUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("SDx Tools", "Stop Play mode before adding the Tools UI to the scene.", "OK");
                return;
            }
            SDxToolsPanel existing = FindInLoadedScenes();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                EditorUtility.DisplayDialog("SDx Tools", "A Tools UI already exists in a loaded scene. Selected it instead of creating a duplicate.", "OK");
                return;
            }
            var go = new GameObject("SDx_Tools_UI");
            Undo.RegisterCreatedObjectUndo(go, "Create SDx Tools UI");
            Undo.AddComponent<SDxToolsPanel>(go);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorUtility.DisplayDialog("SDx Tools",
                "Tools UI added.\n\nSave the scene, then enter Play mode and open the Game tab.\n" +
                "This adds only a UI component. It does not modify MuJoCo, physics, cameras, materials or existing models.", "OK");
        }

        [MenuItem("Tools/SDx/Select Tools UI", false, 2)]
        public static void SelectToolsUI()
        {
            SDxToolsPanel panel = FindInLoadedScenes();
            if (panel == null)
            {
                EditorUtility.DisplayDialog("SDx Tools", "No Tools UI in the loaded scenes. Use Tools > SDx > Create Tools UI first.", "OK");
                return;
            }
            Selection.activeGameObject = panel.gameObject;
            EditorGUIUtility.PingObject(panel.gameObject);
        }

        private static SDxToolsPanel FindInLoadedScenes()
        {
            foreach (SDxToolsPanel panel in Resources.FindObjectsOfTypeAll<SDxToolsPanel>())
            {
                if (panel != null && panel.gameObject.scene.IsValid() && panel.gameObject.scene.isLoaded &&
                    !EditorUtility.IsPersistent(panel) &&
                    (panel.hideFlags & HideFlags.HideAndDontSave) == 0) return panel;
            }
            return null;
        }

        [MenuItem("Tools/SDx/Run UI Self Checks", false, 20)]
        public static void RunSelfChecks()
        {
            // These checks exercise pure state/path logic, not rendering or input.
            int checks = 0;
            Action<bool, string> assert = (condition, name) =>
            {
                if (!condition) throw new InvalidOperationException("Check failed: " + name);
                checks++;
            };
            string temp = Path.Combine(Path.GetTempPath(), "sdx-ui-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = new SDxToolsWindowState();
                assert(state.Mode == SDxToolsWindowMode.Normal, "initial state");
                state.ToggleMinimize(); assert(state.Mode == SDxToolsWindowMode.Minimized, "minimize");
                state.ToggleMinimize(); assert(state.Mode == SDxToolsWindowMode.Normal, "restore from minimized");
                state.ToggleMaximize(); assert(state.Mode == SDxToolsWindowMode.Maximized, "maximize");
                state.ToggleMinimize(); assert(state.Mode == SDxToolsWindowMode.Minimized, "minimize maximized panel");
                state.ToggleMinimize(); assert(state.Mode == SDxToolsWindowMode.Maximized, "restore previous maximized state");
                state.ToggleMaximize(); assert(state.Mode == SDxToolsWindowMode.Normal, "restore normal size");
                state.ToggleMinimize(); state.ToggleMaximize();
                assert(state.Mode == SDxToolsWindowMode.Maximized, "maximize minimized panel");
                state.Restore(); assert(state.Mode == SDxToolsWindowMode.Normal, "explicit restore");
                Directory.CreateDirectory(Path.Combine(temp, "folder with spaces"));
                Directory.CreateDirectory(Path.Combine(temp, "another"));
                var browser = new SDxFolderBrowser();
                assert(browser.Navigate(temp), "navigate to existing folder");
                assert(browser.Directories.Length == 2, "enumerate directories");
                assert(browser.Navigate("folder with spaces"), "relative folder with spaces");
                assert(browser.Up(), "parent navigation");
                assert(browser.CurrentDirectory == Path.GetFullPath(temp), "parent path");
                assert(!browser.Navigate(Path.Combine(temp, "missing")), "reject missing folder");
                assert(browser.CurrentDirectory == Path.GetFullPath(temp), "preserve current folder after failure");
                assert(!string.IsNullOrEmpty(browser.Error), "failure message");
                assert(!browser.Navigate(""), "reject empty folder");
                Debug.Log("[SDx Tools] " + checks + " state/path checks passed. Rendering, native dialogs and IME still require Play-mode testing.");
                EditorUtility.DisplayDialog("SDx Tools", checks + " state/path checks passed.\n\nNow test the controls in Game view during Play mode.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("SDx Tools", ex.Message, "OK");
            }
            finally
            {
                // Delete only the temporary test directory created above.
                if (Directory.Exists(temp))
                {
                    try { Directory.Delete(temp, true); }
                    catch (Exception ex) { Debug.LogWarning("[SDx Tools] Temporary check folder could not be removed: " + ex.Message); }
                }
            }
        }
    }
}
#endif
