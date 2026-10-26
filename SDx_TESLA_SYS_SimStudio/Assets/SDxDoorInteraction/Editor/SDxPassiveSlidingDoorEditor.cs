#if UNITY_EDITOR
using System;
using System.Linq;
using Mujoco;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SDx.DoorInteraction.Editor {
  [CustomEditor(typeof(SDxPassiveSlidingDoor))]
  public sealed class SDxPassiveSlidingDoorEditor : UnityEditor.Editor {
    private enum DoorAction { None, SelectLeaf, Validate, StartClosed, StartHalfOpen, StartOpen, Reverse, Restore }
    private static readonly System.Collections.Generic.HashSet<int> PendingDoors = new System.Collections.Generic.HashSet<int>();

    public override void OnInspectorGUI() {
      var door = (SDxPassiveSlidingDoor)target;
      if (door == null) return;
      DoorAction requested = DoorAction.None;
      EditorGUILayout.LabelField("Passive Sliding Door " + door.PackageVersion, EditorStyles.boldLabel);
      EditorGUILayout.LabelField("Editor GUI fix " + SDxDoorUpgradeWindow.Version, EditorStyles.miniLabel);
      EditorGUILayout.HelpBox("The robot must transmit force through contact. There is no door motor, automatic grip, or runtime Transform animation in this component.", MessageType.Info);
      using (new EditorGUI.DisabledScope(true)) {
        EditorGUILayout.ObjectField("Original (inactive)", door.OriginalDoor, typeof(GameObject), true);
        EditorGUILayout.ObjectField("Fixed Frame", door.Frame, typeof(MjBody), true);
        EditorGUILayout.ObjectField("Moving Leaf", door.Leaf, typeof(MjBody), true);
        EditorGUILayout.ObjectField("Slide Joint", door.Slide, typeof(MjSlideJoint), true);
        EditorGUILayout.FloatField("Opening (m)", door.OpeningMeters);
        EditorGUILayout.FloatField("Travel (m)", door.Travel);
        EditorGUILayout.Vector3Field("World Opening Axis", door.WorldOpeningAxis);
        EditorGUILayout.ObjectField("Front Grasp Target", door.FrontGrasp, typeof(Transform), true);
        EditorGUILayout.ObjectField("Back Grasp Target", door.BackGrasp, typeof(Transform), true);
        EditorGUILayout.ObjectField("Front Approach Target", door.FrontApproach, typeof(Transform), true);
        EditorGUILayout.ObjectField("Back Approach Target", door.BackApproach, typeof(Transform), true);
      }
      using (new EditorGUI.DisabledScope(PendingDoors.Contains(door.GetInstanceID()))) {
        bool draw = EditorGUILayout.Toggle("Show Travel Guide", door.DrawTravelGuide);
        if (draw != door.DrawTravelGuide) { Undo.RecordObject(door, "Door travel guide"); door.DrawTravelGuide = draw; EditorUtility.SetDirty(door); SceneView.RepaintAll(); }
        if (GUILayout.Button("Select Moving Leaf (for Mouse Spring Test)") && door.Leaf != null) requested = DoorAction.SelectLeaf;
        if (GUILayout.Button("Validate This Upgrade")) requested = DoorAction.Validate;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) {
          EditorGUILayout.Space();
          EditorGUILayout.LabelField("Initial Pose (Edit mode only)", EditorStyles.boldLabel);
          EditorGUILayout.HelpBox("These buttons set the starting pose AND joint reference together. They do not animate the door during Play. Set Closed for the requested scenario.", MessageType.None);
          using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("Start Closed")) requested = DoorAction.StartClosed;
            if (GUILayout.Button("Start Half Open")) requested = DoorAction.StartHalfOpen;
            if (GUILayout.Button("Start Open")) requested = DoorAction.StartOpen;
          }
          if (GUILayout.Button("Reverse Opening Direction")) requested = DoorAction.Reverse;
          EditorGUILayout.Space();
          if (GUILayout.Button("Restore Original Door")) requested = DoorAction.Restore;
        }
      }
      EditorGUILayout.Space();
      EditorGUILayout.LabelField("Source MJCF", EditorStyles.boldLabel);
      EditorGUILayout.SelectableLabel(door.GeneratedMjcfPath ?? "", EditorStyles.wordWrappedLabel, GUILayout.Height(40));
      EditorGUILayout.LabelField("Backup Scene", EditorStyles.boldLabel);
      EditorGUILayout.SelectableLabel(door.BackupScenePath ?? "", EditorStyles.wordWrappedLabel, GUILayout.Height(40));
      // Changing Selection or destroying this target in OnInspectorGUI can invalidate
      // the Inspector's own layout. Dispatch only after the drawing pass has finished.
      if (requested != DoorAction.None) QueueAction(door, requested);
      if (Application.isPlaying) Repaint();
    }

    private static void QueueAction(SDxPassiveSlidingDoor door, DoorAction action) {
      if (door == null || action == DoorAction.None) return;
      int instanceId = door.GetInstanceID();
      if (!PendingDoors.Add(instanceId)) return;
      EditorApplication.delayCall += () => {
        try {
          if (door == null || !door.gameObject.scene.IsValid() || !door.gameObject.scene.isLoaded) return;
          bool readOnly = action == DoorAction.SelectLeaf || action == DoorAction.Validate;
          if (EditorApplication.isCompiling || (!readOnly && EditorApplication.isPlayingOrWillChangePlaymode)) {
            Debug.LogWarning("[SDx Doors] Deferred edit cancelled. Stop Play and wait for compilation before retrying.", door);
            return;
          }
          switch (action) {
            case DoorAction.SelectLeaf:
              if (door.Leaf != null) {
                Selection.activeGameObject = door.Leaf.gameObject;
                EditorGUIUtility.PingObject(door.Leaf.gameObject);
              }
              break;
            case DoorAction.Validate: Validate(door); break;
            case DoorAction.StartClosed: SetInitial(door, 0); break;
            case DoorAction.StartHalfOpen: SetInitial(door, door.Travel * 0.5f); break;
            case DoorAction.StartOpen: SetInitial(door, door.Travel); break;
            case DoorAction.Reverse: ReverseDirection(door); break;
            case DoorAction.Restore: Restore(door); break;
          }
        } catch (ExitGUIException) {
          throw;
        } catch (Exception e) {
          Debug.LogException(e);
          EditorUtility.DisplayDialog("SDx Doors", e.Message + "\n\nCheck the Console before retrying.", "OK");
        } finally {
          PendingDoors.Remove(instanceId);
          SceneView.RepaintAll();
          EditorApplication.RepaintHierarchyWindow();
        }
      };
    }

    private static void ReverseDirection(SDxPassiveSlidingDoor door) {
      if (door == null || door.Leaf == null || door.Slide == null)
        throw new InvalidOperationException("Required body/joint references are missing. Run Validate This Upgrade first.");
      SetInitial(door, 0);
      Undo.RecordObject(door, "Reverse sliding direction");
      Undo.RecordObject(door.Slide.transform, "Reverse sliding direction");
      door.LocalOpeningAxis = -door.LocalOpeningAxis;
      door.Slide.transform.localRotation = Quaternion.FromToRotation(Vector3.right, door.LocalOpeningAxis);
      EditorUtility.SetDirty(door);
      EditorSceneManager.MarkSceneDirty(door.gameObject.scene);
      SceneView.RepaintAll();
      Debug.Log("[SDx Doors] Opening direction changed in this Unity scene. The generated source XML is unchanged; edit/export it separately if needed.", door);
    }

    [MenuItem("Tools/SDx/Doors/Validate Selected Upgrade", false, 501)]
    private static void ValidateSelected() {
      var d = Selected();
      if (d == null) { EditorUtility.DisplayDialog("SDx Doors", "Select an upgraded door root or one of its children.", "OK"); return; }
      QueueAction(d, DoorAction.Validate);
    }
    [MenuItem("Tools/SDx/Doors/Restore Selected Original", false, 502)]
    private static void RestoreSelected() {
      var d = Selected();
      if (d == null) { EditorUtility.DisplayDialog("SDx Doors", "Select an upgraded door root or one of its children.", "OK"); return; }
      QueueAction(d, DoorAction.Restore);
    }
    private static SDxPassiveSlidingDoor Selected() {
      return Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponentInParent<SDxPassiveSlidingDoor>();
    }
    internal static void SetInitial(SDxPassiveSlidingDoor d, float value) {
      if (d == null || EditorApplication.isPlayingOrWillChangePlaymode || d.Leaf == null || d.Slide == null) return;
      Undo.RecordObjects(new UnityEngine.Object[] {d, d.Leaf.transform, d.Slide}, "Set sliding door initial state");
      float q = Mathf.Clamp(value, 0, d.Travel);
      d.Leaf.transform.localPosition = d.ClosedLeafLocalPosition + d.LocalOpeningAxis * q;
      // MjSlideJoint serializes Configuration as MJCF ref. The pose must change with ref.
      d.Slide.Configuration = q;
      d.Slide.Velocity = 0;
      d.InitialOpening = q;
      EditorUtility.SetDirty(d);
      EditorUtility.SetDirty(d.Slide);
      EditorSceneManager.MarkSceneDirty(d.gameObject.scene);
      SceneView.RepaintAll();
    }
    private static void Validate(SDxPassiveSlidingDoor d) {
      var problems = new System.Collections.Generic.List<string>();
      if (d.OriginalDoor == null) problems.Add("Original door backup reference is missing.");
      else if (d.OriginalDoor.activeInHierarchy) problems.Add("Original door is active: duplicate collision geometry!");
      if (d.Leaf == null || d.Frame == null || d.Slide == null) problems.Add("Required body/joint references are missing.");
      if (d.GetComponentsInChildren<MjBody>(true).Length != 2) problems.Add("Expected two bodies (fixed frame and sliding leaf).");
      if (d.GetComponentsInChildren<MjSlideJoint>(true).Length != 1) problems.Add("Expected one slide joint.");
      if (d.GetComponentsInChildren<MjFreeJoint>(true).Length != 0) problems.Add("Unexpected FreeJoint.");
      if (d.GetComponentsInChildren<MjActuator>(true).Length != 0) problems.Add("Unexpected door actuator: this model is passive.");
      if (d.GetComponentsInChildren<MjGlobalSettings>(true).Length != 0) problems.Add("Door has duplicate global settings.");
      if (d.FrontGrasp == null || d.BackGrasp == null) problems.Add("Handle target sites are missing.");
      if (Mathf.Abs(Vector3.Dot(d.WorldOpeningAxis, Vector3.up)) > 0.01f) problems.Add("Opening axis is not horizontal.");
      if (!Application.isPlaying && d.Leaf != null && d.Slide != null) {
        if ((d.Leaf.transform.localPosition - (d.ClosedLeafLocalPosition + d.LocalOpeningAxis * d.InitialOpening)).sqrMagnitude > 0.000001f)
          problems.Add("Leaf Transform was edited independently. Use Start Closed/Open buttons.");
        if (Mathf.Abs(d.Slide.Configuration - d.InitialOpening) > 0.0001f) problems.Add("Joint reference and start pose differ.");
      }
      if (Mathf.Abs(Time.fixedDeltaTime - 0.001f) > 0.00001f)
        Debug.LogWarning("[SDx Doors] Fixed Timestep is not 0.001 s. This tool does not change project physics settings.", d);
      if (problems.Count == 0) {
        Debug.Log("[SDx Doors] STRUCTURE CHECK PASSED. 1 passive slide, 2 bodies, no door actuator/global settings. This is not a gripper or world-clearance test.", d);
        EditorUtility.DisplayDialog("SDx Doors", "Structure check passed.\n\nNow inspect the opening path and test physical movement. FR3 grasping is not validated by this check.", "OK");
      } else {
        string message = string.Join("\n", problems);
        Debug.LogError("[SDx Doors] " + message, d);
        EditorUtility.DisplayDialog("SDx Doors", message, "OK");
      }
    }
    private static void Restore(SDxPassiveSlidingDoor d) {
      if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("SDx Doors", "Stop Play first.", "OK"); return; }
      if (d.OriginalDoor == null) { EditorUtility.DisplayDialog("SDx Doors", "Original reference is missing. Restore the backup scene instead.", "OK"); return; }
      if (!EditorUtility.DisplayDialog("SDx Doors", "Remove this replacement and reactivate the unchanged original door? Generated assets will be retained.", "Restore", "Cancel")) return;
      var original = d.OriginalDoor;
      var scene = d.gameObject.scene;
      Undo.IncrementCurrentGroup();
      int group = Undo.GetCurrentGroup();
      Undo.SetCurrentGroupName("Restore original AWS door");
      Undo.RecordObject(original, "Reactivate original door");
      original.SetActive(true);
      Undo.DestroyObjectImmediate(d.gameObject);
      Undo.CollapseUndoOperations(group);
      EditorSceneManager.MarkSceneDirty(scene);
      Selection.activeGameObject = original;
    }
  }
}
#endif
