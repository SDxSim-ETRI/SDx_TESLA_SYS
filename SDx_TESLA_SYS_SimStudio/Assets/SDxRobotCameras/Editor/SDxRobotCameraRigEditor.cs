#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SDx.SimStudio.Cameras.Editor
{
    [CustomEditor(typeof(SDxRobotCameraRig))]
    public sealed class SDxRobotCameraRigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var rig = (SDxRobotCameraRig)target;
            EditorGUILayout.HelpBox("Robot Follow Cameras + Free 1.1.1\nFront/Left/Right/Back are robot-relative. The Husky forward axis is +X, not Unity's usual +Z. Camera changes do not drive the robot. Free: WASD, Q/E height, RMB to look, wheel dolly, Shift fast. RMB lock transition is briefly guarded to discard cursor-centering deltas. Enter opens chat; Esc releases mouse. No video recording.", MessageType.Info);
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Current view", rig.ActiveView.ToString());
            if (GUILayout.Button("Validate Camera References"))
                EditorApplication.delayCall += SDxRobotCameraInstaller.ValidateRig;
            if (!Application.isPlaying && GUILayout.Button("Preview Camera Positions (Edit mode)"))
            {
                EditorApplication.delayCall += () =>
                {
                    if (rig == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
                    for (int i = 0; i < 4; ++i)
                    {
                        Camera camera = rig.GetCamera((SDxRobotCameraView)i);
                        if (camera != null) Undo.RecordObjects(new Object[] { camera, camera.transform }, "Preview robot camera");
                    }
                    if (rig.FollowListener != null)
                        Undo.RecordObjects(new Object[] { rig.FollowListener, rig.FollowListener.transform }, "Preview follow audio");
                    rig.PreviewNow(); EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
                    SceneView.RepaintAll();
                };
            }
            if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < 5; ++i)
                {
                    SDxRobotCameraView view = (SDxRobotCameraView)i;
                    if (GUILayout.Button(view.ToString())) rig.SelectView(view);
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
#endif
