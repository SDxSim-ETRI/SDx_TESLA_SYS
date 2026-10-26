using Mujoco;
using UnityEngine;

namespace SDx.DoorInteraction {
  // Metadata and read-only targets. This component never writes a simulated Transform,
  // qpos, control signal, or an artificial grasp constraint during Play.
  [DisallowMultipleComponent]
  public sealed class SDxPassiveSlidingDoor : MonoBehaviour {
    [HideInInspector] public string PackageVersion = "1.0.0";
    [HideInInspector] public GameObject OriginalDoor;
    [HideInInspector] public MjBody Frame;
    [HideInInspector] public MjBody Leaf;
    [HideInInspector] public MjSlideJoint Slide;
    [HideInInspector] public Transform FrontGrasp;
    [HideInInspector] public Transform BackGrasp;
    [HideInInspector] public Transform FrontApproach;
    [HideInInspector] public Transform BackApproach;
    [HideInInspector] public Vector3 ClosedLeafLocalPosition;
    [HideInInspector] public Vector3 LocalOpeningAxis = Vector3.forward;
    [HideInInspector] public float Travel = 2.4f;
    [HideInInspector] public float InitialOpening;
    [HideInInspector] public string GeneratedMjcfPath;
    [HideInInspector] public string BackupScenePath;
    [HideInInspector] public bool DrawTravelGuide = true;

    public float OpeningMeters {
      get { return Application.isPlaying && Slide != null ? Slide.Configuration : InitialOpening; }
    }
    public float OpenFraction { get { return Travel > 0 ? Mathf.Clamp01(OpeningMeters / Travel) : 0; } }
    public bool IsFullyOpen { get { return OpeningMeters >= Travel - 0.02f; } }
    public Vector3 WorldOpeningAxis { get { return transform.TransformDirection(LocalOpeningAxis).normalized; } }

    private void OnDrawGizmosSelected() {
      if (!DrawTravelGuide) return;
      Matrix4x4 old = Gizmos.matrix;
      Color oldColor = Gizmos.color;
      Gizmos.matrix = transform.localToWorldMatrix;
      Vector3 center = ClosedLeafLocalPosition + new Vector3(-0.005f, 1.0626447f, 0.0017305f);
      // Includes the two handles. This is only a clearance visualization, not a collider.
      Vector3 size = new Vector3(0.204f, 2.1240914f, 2.3372962f);
      Vector3 offset = LocalOpeningAxis * Travel;
      Gizmos.color = new Color(0.95f, 0.65f, 0.1f, 1f);
      Gizmos.DrawWireCube(center, size);
      Gizmos.color = new Color(0.1f, 0.85f, 0.9f, 1f);
      Gizmos.DrawWireCube(center + offset, size);
      Gizmos.DrawWireCube(center + offset * 0.5f, size + new Vector3(Mathf.Abs(offset.x), Mathf.Abs(offset.y), Mathf.Abs(offset.z)));
      Vector3 a = center + Vector3.up * 1.18f;
      Vector3 b = a + offset;
      Gizmos.DrawLine(a, b);
      Gizmos.DrawLine(b, b - LocalOpeningAxis * 0.15f + Vector3.up * 0.06f);
      Gizmos.DrawLine(b, b - LocalOpeningAxis * 0.15f - Vector3.up * 0.06f);
      Gizmos.matrix = old;
      Gizmos.color = oldColor;
    }
  }
}
