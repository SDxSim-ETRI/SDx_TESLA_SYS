using UnityEngine;

namespace SDx.SimStudio.Cameras
{
    // Pure camera math; no robot/physics writes and no dependency on input devices.
    public static class SDxFreeCameraMath
    {
        public static void ReadLookAngles(Quaternion rotation, out float yaw, out float pitch)
        {
            Vector3 forward = rotation * Vector3.forward;
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        public static Vector3 FlyDirection(Quaternion rotation, float lateral, float vertical, float forward)
        {
            Vector3 direction = rotation * new Vector3(lateral, 0, forward) + Vector3.up * vertical;
            return Vector3.ClampMagnitude(direction, 1f);
        }
    }
}
