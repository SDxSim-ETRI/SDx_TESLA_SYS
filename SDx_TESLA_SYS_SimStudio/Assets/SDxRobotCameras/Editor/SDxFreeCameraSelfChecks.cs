#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace SDx.SimStudio.Cameras.Editor
{
    public static class SDxFreeCameraSelfChecks
    {
        [MenuItem("Tools/SDx/Cameras/Run Free Camera Math Self Checks", false, 21)]
        public static void Run()
        {
            int count = 0;
            Action<bool, string> verify = (ok, name) =>
            {
                if (!ok) throw new InvalidOperationException(name);
                ++count;
            };
            try
            {
                verify((int)SDxRobotCameraView.Front == 0 && (int)SDxRobotCameraView.Left == 1 &&
                    (int)SDxRobotCameraView.Right == 2 && (int)SDxRobotCameraView.Back == 3 &&
                    (int)SDxRobotCameraView.Free == 4, "Preserve serialized enum values");
                for (int y = -180; y < 180; y += 30)
                    for (int p = -80; p <= 80; p += 20)
                    {
                        Quaternion rotation = Quaternion.Euler(p, y, 0);
                        float yaw, pitch;
                        SDxFreeCameraMath.ReadLookAngles(rotation, out yaw, out pitch);
                        Vector3 f = rotation * Vector3.forward;
                        Vector3 recovered = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
                        verify(Vector3.Distance(f, recovered) < .0001f, "Free look keeps the forward direction");
                        verify(Vector3.Distance(SDxFreeCameraMath.FlyDirection(rotation, 0, 0, 1), f) < .0001f,
                            "W follows the camera forward axis");
                        verify(Vector3.Distance(SDxFreeCameraMath.FlyDirection(rotation, 1, 0, 0),
                            rotation * Vector3.right) < .0001f, "D follows the camera right axis");
                        verify(SDxFreeCameraMath.FlyDirection(rotation, 1, 1, 1).magnitude <= 1.00001f,
                            "Combined inputs do not exceed the speed cap");
                        verify(SDxFreeCameraMath.FlyDirection(rotation, 0, 0, 0) == Vector3.zero,
                            "No input means no drift");
                    }
                verify(SDxFreeCameraMath.FlyDirection(Quaternion.identity, 0, 1, 0) == Vector3.up,
                    "E moves along world up");
                verify(SDxFreeCameraMath.FlyDirection(Quaternion.identity, 0, -1, 0) == Vector3.down,
                    "Q moves along world down");
                Debug.Log("[SDx Cameras] " + count + " Free camera math checks passed. " +
                    "Rendering, input focus, cursor locking and Free-view switching must still be tested in Play.");
                EditorUtility.DisplayDialog("Free Camera Math Checks", count + " checks passed.\n\n" +
                    "Test Free pose continuity, WASD, RMB, wheel and text-input blocking in Game view.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Free Camera Math Checks", ex.Message, "OK");
            }
        }
    }
}
#endif
