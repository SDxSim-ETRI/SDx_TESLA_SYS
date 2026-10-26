#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace SDx.SimStudio.Cameras.Editor
{
    public static class SDxCameraSelfChecks
    {
        [MenuItem("Tools/SDx/Cameras/Run Camera Math Self Checks", false, 20)]
        public static void Run()
        {
            int count = 0;
            Action<bool, string> assert = (ok, message) => { if (!ok) throw new InvalidOperationException(message); ++count; };
            try
            {
                Vector3 x = Vector3.right;
                assert(Vector3.Distance(SDxCameraPoseMath.ViewDirection(x, SDxRobotCameraView.Front), Vector3.right) < 0.00001f, "Husky front = +X");
                assert(Vector3.Distance(SDxCameraPoseMath.ViewDirection(x, SDxRobotCameraView.Left), Vector3.forward) < 0.00001f, "Husky left = +Z");
                assert(Vector3.Distance(SDxCameraPoseMath.ViewDirection(x, SDxRobotCameraView.Right), Vector3.back) < 0.00001f, "Husky right = -Z");
                assert(Vector3.Distance(SDxCameraPoseMath.ViewDirection(x, SDxRobotCameraView.Back), Vector3.left) < 0.00001f, "Husky back = -X");
                Vector3 focus = new Vector3(.581f, .93228f, 1.635f);
                for (int angle = 0; angle < 360; angle += 15)
                {
                    Quaternion yaw = Quaternion.Euler(0, angle, 0);
                    Vector3 forward = SDxCameraPoseMath.PlanarForward(yaw, x, x);
                    for (int i = 0; i < 4; ++i)
                    {
                        Pose pose = SDxCameraPoseMath.ViewPose(focus, forward, (SDxRobotCameraView)i, 2.1f, 1.4f, 0);
                        Vector3 delta = pose.position - focus;
                        assert(Mathf.Abs(Vector3.ProjectOnPlane(delta, Vector3.up).magnitude - 2.1f) < .00001f, "Horizontal radius");
                        assert(Mathf.Abs(delta.y - 1.4f) < .00001f, "Elevation");
                        assert(Vector3.Dot(pose.rotation * Vector3.forward, -delta.normalized) > .99999f, "Look at robot centre");
                        assert(Mathf.Abs(Vector3.Dot(pose.rotation * Vector3.right, Vector3.up)) < .00001f, "Level horizon");
                    }
                }
                assert(SDxCameraPoseMath.FollowWeight(0, .016f) == 1f, "Instant tracking option");
                assert(Mathf.Approximately(SDxCameraPoseMath.FollowWeight(12, 0), 0), "Zero-delta damping");
                assert(SDxCameraPoseMath.PlanarForward(Quaternion.identity, Vector3.up, x) == x, "Near-vertical fallback");
                Debug.Log("[SDx Cameras] " + count + " camera-math checks passed. This is not a rendered scene/occlusion or MuJoCo test.");
                EditorUtility.DisplayDialog("Camera Math Checks", count + " checks passed.\n\nNow verify all four views and scenario reset in Play mode.", "OK");
            }
            catch (Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog("Camera Math Checks", ex.Message, "OK"); }
        }
    }
}
#endif
