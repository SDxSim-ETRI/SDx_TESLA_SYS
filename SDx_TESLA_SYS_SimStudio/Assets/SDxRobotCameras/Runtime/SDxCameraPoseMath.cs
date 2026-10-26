using UnityEngine;

namespace SDx.SimStudio.Cameras
{
    public enum SDxRobotCameraView { Front = 0, Left = 1, Right = 2, Back = 3, Free = 4 }

    /// <summary>Camera-only geometry. This code never writes to the tracked robot.</summary>
    public static class SDxCameraPoseMath
    {
        public static Vector3 PlanarForward(Quaternion rotation, Vector3 localForward, Vector3 fallback)
        {
            Vector3 f = Vector3.ProjectOnPlane(rotation * localForward, Vector3.up);
            if (f.sqrMagnitude < 0.000001f)
                f = Vector3.ProjectOnPlane(fallback, Vector3.up);
            return f.sqrMagnitude < 0.000001f ? Vector3.right : f.normalized;
        }

        public static Vector3 ViewDirection(Vector3 forward, SDxRobotCameraView view)
        {
            forward = PlanarForward(Quaternion.identity, forward, Vector3.right);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            switch (view)
            {
                case SDxRobotCameraView.Left: return -right;
                case SDxRobotCameraView.Right: return right;
                case SDxRobotCameraView.Back: return -forward;
                default: return forward;
            }
        }

        public static Pose ViewPose(Vector3 focus, Vector3 forward, SDxRobotCameraView view,
                                    float distance, float elevation, float azimuthDegrees)
        {
            Vector3 outward = Quaternion.AngleAxis(azimuthDegrees, Vector3.up) * ViewDirection(forward, view);
            Vector3 position = focus + outward * Mathf.Max(0.25f, distance) + Vector3.up * Mathf.Max(0.05f, elevation);
            return new Pose(position, Quaternion.LookRotation(focus - position, Vector3.up));
        }

        public static float FollowWeight(float damping, float deltaTime)
        {
            return damping <= 0f ? 1f : 1f - Mathf.Exp(-damping * Mathf.Max(0f, deltaTime));
        }
    }
}
