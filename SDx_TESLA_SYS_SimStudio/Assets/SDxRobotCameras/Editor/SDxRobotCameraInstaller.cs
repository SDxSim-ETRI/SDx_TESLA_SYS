#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SDx.SimStudio.UI;

namespace SDx.SimStudio.Cameras.Editor
{
    public static class SDxRobotCameraInstaller
    {
        private const string RootName = "SDx_RobotCameras";
        private static bool pending;

        // Treat a fully configured rig as an already installed scene feature.
        // The menu is available only for the first creation or an incomplete rig.
        private static bool HasCompletedRig(Scene scene, out SDxRobotCameraRig result)
        {
            result = null;
            if (!scene.IsValid() || !scene.isLoaded) return false;
            var rigs = InScene<SDxRobotCameraRig>(scene);
            if (rigs.Length != 1) return false;
            var rig = rigs[0];
            string message;
            if (rig == null || !rig.ValidateReferences(out message)) return false;
            var bridge = rig.GetComponent<SDxRobotCameraUiBridge>();
            if (bridge == null || bridge.Rig != rig || bridge.ToolsUI == null ||
                bridge.ToolsUI.gameObject.scene != scene || !bridge.ToolsUI.showCameraGroup)
                return false;
            result = rig;
            return true;
        }

        [MenuItem("Tools/SDx/Cameras/Create or Update Robot Follow Cameras", true)]
        private static bool CanCreateOrUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path)) return false;
            SDxRobotCameraRig completed;
            return !HasCompletedRig(scene, out completed);
        }

        [MenuItem("Tools/SDx/Cameras/Create or Update Robot Follow Cameras", false, 1)]
        public static void CreateOrUpdate()
        {
            // Guard programmatic calls in addition to disabling the completed menu.
            SDxRobotCameraRig completed;
            if (HasCompletedRig(SceneManager.GetActiveScene(), out completed))
            {
                Selection.activeGameObject = completed.gameObject;
                Debug.Log("[SDx OneTime] Follow cameras are already configured; no update or backup made.", completed);
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorUtility.DisplayDialog("Robot Cameras", "Stop Play and save the current scene first.", "OK"); return; }
            if (pending) return;
            pending = true;
            EditorApplication.delayCall += PrepareDeferred;
        }

        private static T[] InScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        }

        private static void PrepareDeferred()
        {
            pending = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            int undoGroup = -1;
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                    throw new InvalidOperationException("Open and save the current MainScene first.");
                // A scene may have changed between the menu click and delayCall.
                SDxRobotCameraRig alreadyConfigured;
                if (HasCompletedRig(scene, out alreadyConfigured))
                {
                    Selection.activeGameObject = alreadyConfigured.gameObject;
                    Debug.Log("[SDx OneTime] Camera setup skipped: saved rig is already complete.", alreadyConfigured);
                    return;
                }
                var transforms = InScene<Transform>(scene);
                var targets = transforms.Where(t => t.name == "base_link" && t.gameObject.activeInHierarchy &&
                    t.GetComponentsInChildren<Transform>(true).Any(x => x.name == "front_bumper_link") &&
                    t.GetComponentsInChildren<Transform>(true).Any(x => x.name == "rear_bumper_link")).ToArray();
                if (targets.Length != 1) throw new InvalidOperationException("Expected one active Husky base_link with front/rear bumper children; found " + targets.Length + ". No scene changes made.");
                Transform target = targets[0];
                var children = target.GetComponentsInChildren<Transform>(true);
                Transform front = children.Single(t => t.name == "front_bumper_link");
                Transform rear = children.Single(t => t.name == "rear_bumper_link");
                Vector3 axis = target.InverseTransformDirection(front.position - rear.position);
                if (axis.sqrMagnitude < 0.01f) throw new InvalidOperationException("Cannot determine the robot forward direction from its bumper positions.");
                axis.Normalize();
                var panels = InScene<SDxToolsPanel>(scene).Where(x => x.gameObject.activeInHierarchy).ToArray();
                if (panels.Length != 1) throw new InvalidOperationException("Expected one active SDx Tools Panel. Keep the existing Tools UI in this scene.");
                SDxToolsPanel ui = panels[0];
                var rigs = InScene<SDxRobotCameraRig>(scene);
                if (rigs.Length > 1) throw new InvalidOperationException("More than one camera rig exists. Remove duplicates before updating.");
                SDxRobotCameraRig rig = rigs.FirstOrDefault();
                if (rig == null && scene.GetRootGameObjects().Any(g => g.name == RootName))
                    throw new InvalidOperationException("A different object already uses the name " + RootName + ". Rename it or restore the previous rig first.");
                Camera source = rig == null ? null : rig.OriginalCamera;
                if (source == null)
                {
                    var cameras = InScene<Camera>(scene).Where(c => c.gameObject.activeInHierarchy &&
                        (rig == null || !c.transform.IsChildOf(rig.transform)) && c.targetTexture == null).ToArray();
                    source = cameras.FirstOrDefault(c => c.name == "Main Camera");
                    if (source == null)
                    {
                        var mains = cameras.Where(c => c.CompareTag("MainCamera")).ToArray();
                        if (mains.Length == 1) source = mains[0];
                    }
                    if (source == null) throw new InvalidOperationException("Cannot identify the original Main Camera in this scene.");
                }
                if (rig != null)
                {
                    if (rig.FollowListener != null && !rig.FollowListener.transform.IsChildOf(rig.transform))
                        throw new InvalidOperationException("Follow Listener must belong to the camera rig, not the original Main Camera.");
                    for (int i = 0; i < 5; ++i)
                    {
                        Camera c = rig.GetCamera((SDxRobotCameraView)i);
                        if (c != null && !c.transform.IsChildOf(rig.transform))
                            throw new InvalidOperationException("A rig camera reference points outside its hierarchy. Correct the reference first.");
                    }
                }
                if (!EditorUtility.DisplayDialog("Robot Follow Cameras",
                    "A scene backup will be saved first.\n\nThis creates four follow cameras plus Free and binds the existing Tools UI Camera buttons.\n\nTarget: " + target.name +
                    "\nRobot local forward: " + axis.ToString("F3") +
                    "\n\nThe original Main Camera is kept. Physics, robot controls and scenario paths are not modified.\n\nDefault view: Front. Continue?", "Create / Update", "Cancel")) return;

                string backupDir = "Assets/SDxRobotCameras/Backups";
                Directory.CreateDirectory(backupDir);
                AssetDatabase.Refresh();
                string backup = AssetDatabase.GenerateUniqueAssetPath(backupDir + "/" + scene.name +
                    "_before_cameras_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity");
                if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Scene backup failed. No cameras created.");

                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Create SDx Robot Follow Cameras");
                if (rig == null)
                {
                    var go = new GameObject(RootName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    Undo.RegisterCreatedObjectUndo(go, "Create camera rig");
                    rig = Undo.AddComponent<SDxRobotCameraRig>(go);
                }
                Undo.RecordObject(rig, "Configure camera rig");
                rig.FollowTarget = target; rig.OriginalCamera = source; rig.LocalForwardAxis = axis;
                // Do not overwrite the user's framing/smoothing tuning when updating an existing rig.
                rig.FrontCamera = EnsureCamera(rig, rig.FrontCamera, "Camera_Front", source);
                rig.LeftCamera = EnsureCamera(rig, rig.LeftCamera, "Camera_Left", source);
                rig.RightCamera = EnsureCamera(rig, rig.RightCamera, "Camera_Right", source);
                rig.BackCamera = EnsureCamera(rig, rig.BackCamera, "Camera_Back", source);
                rig.FreeCamera = EnsureCamera(rig, rig.FreeCamera, "Camera_Free", source);
                if (rig.FollowListener == null)
                {
                    var audio = new GameObject("CameraAudioListener");
                    Undo.RegisterCreatedObjectUndo(audio, "Create follow audio listener");
                    Undo.SetTransformParent(audio.transform, rig.transform, "Parent follow audio listener");
                    rig.FollowListener = Undo.AddComponent<AudioListener>(audio);
                }
                Undo.RecordObject(rig.FollowListener, "Configure follow listener");
                rig.FollowListener.enabled = false;
                var bridge = rig.GetComponent<SDxRobotCameraUiBridge>();
                if (bridge == null) bridge = Undo.AddComponent<SDxRobotCameraUiBridge>(rig.gameObject);
                Undo.RecordObject(bridge, "Bind Camera group");
                bridge.ToolsUI = ui; bridge.Rig = rig;
                Undo.RecordObject(ui, "Show Camera group"); ui.showCameraGroup = true;
                for (int i = 0; i < 5; ++i) Undo.RecordObject(rig.GetCamera((SDxRobotCameraView)i).transform, "Preview camera pose");
                Undo.RecordObject(rig.FollowListener.transform, "Preview listener pose");
                rig.PreviewNow();
                string result;
                if (!rig.ValidateReferences(out result)) throw new InvalidOperationException(result);
                EditorUtility.SetDirty(rig); EditorUtility.SetDirty(bridge); EditorUtility.SetDirty(ui);
                Undo.CollapseUndoOperations(undoGroup); undoGroup = -1;
                EditorSceneManager.MarkSceneDirty(scene);
                Selection.activeGameObject = rig.gameObject;
                Debug.Log("[SDx Cameras] Setup complete. Backup: " + backup +
                    ". Save the scene, press Play; Camera is the top group; Free begins at the currently displayed pose. No robot/physics files changed.", rig);
                EditorUtility.DisplayDialog("Robot cameras prepared",
                    "Save the scene (Ctrl+S), then press Play.\n\nUse Camera > Front / Left / Right / Back / Free at the top. Front is the default.\n\nThe original Main Camera remains unchanged in Edit mode.\n\nBackup: " + backup, "OK");
            }
            catch (Exception ex)
            {
                if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Robot Cameras", ex.Message, "OK");
            }
        }

        private static Camera EnsureCamera(SDxRobotCameraRig rig, Camera existing, string name, Camera source)
        {
            if (existing != null)
            {
                Undo.RecordObject(existing, "Disable edit-mode follow camera");
                existing.enabled = false;
                return existing;
            }
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            Undo.SetTransformParent(go.transform, rig.transform, "Parent " + name);
            go.tag = "MainCamera";
            Camera camera = Undo.AddComponent<Camera>(go);
            camera.CopyFrom(source);
            camera.enabled = false; camera.targetTexture = null;
            camera.rect = new Rect(0, 0, 1, 1); camera.orthographic = false;
            camera.ResetProjectionMatrix(); camera.ResetWorldToCameraMatrix();
            Skybox sky = source.GetComponent<Skybox>();
            if (sky != null)
            {
                Skybox copy = Undo.AddComponent<Skybox>(go);
                copy.material = sky.material; copy.enabled = sky.enabled;
            }
            return camera;
        }

        [MenuItem("Tools/SDx/Cameras/Select Robot Follow Cameras", false, 2)]
        public static void SelectRig()
        {
            var rig = InScene<SDxRobotCameraRig>(SceneManager.GetActiveScene()).FirstOrDefault();
            if (rig != null) Selection.activeGameObject = rig.gameObject;
            else EditorUtility.DisplayDialog("Robot Cameras", "Run Create or Update Robot Follow Cameras first.", "OK");
        }

        [MenuItem("Tools/SDx/Cameras/Validate Current Camera Rig", false, 3)]
        public static void ValidateRig()
        {
            var rigs = InScene<SDxRobotCameraRig>(SceneManager.GetActiveScene());
            if (rigs.Length != 1) { Debug.LogError("[SDx Cameras] Expected one rig, found " + rigs.Length); return; }
            SDxRobotCameraRig rig = rigs[0];
            string result;
            if (!rig.ValidateReferences(out result)) { Debug.LogError("[SDx Cameras] " + result, rig); return; }
            var bridge = rig.GetComponent<SDxRobotCameraUiBridge>();
            if (bridge == null || bridge.ToolsUI == null || bridge.Rig != rig)
            { Debug.LogError("[SDx Cameras] UI bridge reference is incomplete.", rig); return; }
            if (Application.isPlaying)
            {
                int rendering = Enumerable.Range(0, 5).Count(i => rig.GetCamera((SDxRobotCameraView)i) != null && rig.GetCamera((SDxRobotCameraView)i).isActiveAndEnabled);
                int listeners = InScene<AudioListener>(rig.gameObject.scene).Count(a => a.isActiveAndEnabled);
                if (!rig.IsReady || rendering != 1 || listeners != 1 || rig.OriginalCamera.enabled)
                { Debug.LogError("[SDx Cameras] Runtime check failed. Ready=" + rig.IsReady + ", managed cameras=" + rendering + ", listeners=" + listeners, rig); return; }
            }
            Debug.Log("[SDx Cameras] STRUCTURE CHECK PASSED. Active view=" + rig.ActiveView +
                ". Target=" + rig.FollowTarget.name + ". This does not test image framing or obstacle occlusion.", rig);
        }

        [MenuItem("Tools/SDx/Cameras/Remove Follow Cameras (Keep Main Camera)", false, 30)]
        public static void RemoveRig()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                Scene scene = SceneManager.GetActiveScene();
                var rigs = InScene<SDxRobotCameraRig>(scene);
                if (rigs.Length != 1) return;
                if (!EditorUtility.DisplayDialog("Remove follow cameras", "Remove only SDx_RobotCameras? The original Main Camera, robot and scenario are kept.", "Remove", "Cancel")) return;
                var bridge = rigs[0].GetComponent<SDxRobotCameraUiBridge>();
                if (bridge != null && bridge.ToolsUI != null)
                {
                    Undo.RecordObject(bridge.ToolsUI, "Hide Camera group");
                    bridge.ToolsUI.showCameraGroup = false;
                    EditorUtility.SetDirty(bridge.ToolsUI);
                }
                Undo.DestroyObjectImmediate(rigs[0].gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
            };
        }
    }
}
#endif
