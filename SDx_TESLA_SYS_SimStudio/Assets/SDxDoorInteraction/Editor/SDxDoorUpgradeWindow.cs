#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Mujoco;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SDx.DoorInteraction.Editor {
  public sealed class SDxDoorUpgradeWindow : EditorWindow {
    private MjBody target;
    private int direction;
    private int railSide;
    private float travel = 2.4f;
    private float railOffset = 0.16f;
    private float mass = 18f;
    private float damping = 25f;
    private float friction = 3f;
    private bool advanced;
    private Vector2 scroll;
    private const string Token = "sdxdoor_";
    private const string SourcePrefix = "aws_robomaker_residential_FoldingDoor_01_FoldingDoor_01_";
    internal const string Version = "1.0.1";
    private bool upgradePending;

    [MenuItem("Tools/SDx/Doors/Upgrade Selected Folding Door...", false, 500)]
    public static void OpenWindow() {
      var w = GetWindow<SDxDoorUpgradeWindow>("Sliding Door Upgrade");
      w.minSize = new Vector2(440, 460);
      w.UseSelection();
      w.Show();
    }

    private void UseSelection() {
      Transform t = Selection.activeTransform;
      while (t != null) {
        var b = t.GetComponent<MjBody>();
        if (b != null && IsOriginalName(t.name)) {
          target = b;
          direction = t.name.EndsWith("002", StringComparison.Ordinal) ? 1 : 0;
          return;
        }
        t = t.parent;
      }
      target = null;
    }

    // Never import assets, save scenes or change Selection inside this GUI pass.
    // Those operations can re-enter IMGUI and invalidate its layout stack.
    private void OnGUI() {
      bool requested = false;
      using (var view = new EditorGUILayout.ScrollViewScope(scroll)) {
        scroll = view.scrollPosition;
        EditorGUILayout.LabelField("AWS FoldingDoor_01 -> Passive Sliding Door", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Editor GUI fix " + Version, EditorStyles.miniLabel);
        EditorGUILayout.HelpBox("Only the selected doorway is replaced. The whole world is NOT reimported. " +
          "The original is kept inactive, and a scene backup is saved before conversion. " +
          "This is a rigid sliding-door simplification, NOT a folding mechanism.", MessageType.Info);
        using (new EditorGUI.DisabledScope(upgradePending || EditorApplication.isPlayingOrWillChangePlaymode)) {
          if (GUILayout.Button("Use Selected Original Door")) UseSelection();
          target = (MjBody)EditorGUILayout.ObjectField("Original Door", target, typeof(MjBody), true);
          direction = EditorGUILayout.Popup("Opening Direction", direction, new[] {"Local +Z (MJCF +Y)", "Local -Z (MJCF -Y)"});
          railSide = EditorGUILayout.Popup("Rail Side", railSide, new[] {"Local +X side", "Local -X side"});
          travel = EditorGUILayout.FloatField("Travel (meters)", travel);
          railOffset = EditorGUILayout.FloatField("Rail Offset (meters)", railOffset);
          EditorGUILayout.HelpBox("Defaults: travel 2.4 m; rail offset 0.16 m. A full-width leaf needs empty space beside the doorway. " +
            "Rail offset separates the moving leaf/handles from the stationary jambs. " +
            "Do not interpret the arrows as a collision-clearance guarantee.", MessageType.Warning);
          advanced = EditorGUILayout.Foldout(advanced, "Physical Parameters (simulation assumptions)", true);
          if (advanced) {
            mass = EditorGUILayout.FloatField("Leaf Mass (kg)", mass);
            damping = EditorGUILayout.FloatField("Joint Damping (N s/m)", damping);
            friction = EditorGUILayout.FloatField("Joint Friction Loss (N)", friction);
          }
          if (target != null) {
            Vector3 axis = target.transform.TransformDirection(Vector3.forward * (direction == 0 ? 1 : -1));
            EditorGUILayout.LabelField("World Opening Axis", axis.ToString("F3"));
            EditorGUILayout.LabelField("Current World Position", target.transform.position.ToString("F3"));
          }
          using (new EditorGUI.DisabledScope(target == null)) {
            requested = GUILayout.Button("Create Backup and Upgrade This Door", GUILayout.Height(34));
          }
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
          EditorGUILayout.HelpBox("Stop Play before changing the model.", MessageType.Warning);
        EditorGUILayout.LabelField("After conversion", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Select the new root to inspect targets, set a starting pose, or restore the original.", EditorStyles.wordWrappedLabel);
      }
      // All layout scopes have ended; the actual work runs on the next Editor delayCall.
      if (requested) QueueUpgrade();
    }

    private sealed class UpgradeRequest {
      internal readonly MjBody Target;
      internal readonly int Direction, RailSide;
      internal readonly float Travel, RailOffset, Mass, Damping, Friction;
      internal UpgradeRequest(SDxDoorUpgradeWindow w) {
        Target = w.target; Direction = w.direction; RailSide = w.railSide;
        Travel = w.travel; RailOffset = w.railOffset; Mass = w.mass;
        Damping = w.damping; Friction = w.friction;
      }
    }

    private void QueueUpgrade() {
      if (upgradePending || target == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
      // Snapshot the requested object and parameters; a later selection cannot redirect the action.
      var request = new UpgradeRequest(this);
      upgradePending = true;
      Repaint();
      EditorApplication.delayCall += () => {
        // Closing the tool before dispatch cancels the queued operation.
        if (this == null) return;
        try {
          if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Upgrade cancelled: wait for compilation and stop Play before retrying.");
          Upgrade(request);
        } catch (ExitGUIException) {
          // Unity uses this exception to terminate a GUI event. Never swallow it as a model failure.
          throw;
        } catch (Exception e) {
          Debug.LogException(e);
          EditorUtility.DisplayDialog("SDx Doors", e.Message +
            "\n\nReview the first Console error and current door state before retrying. No whole-world reimport is required.", "OK");
        } finally {
          if (this != null) {
            upgradePending = false;
            Repaint();
          }
        }
      };
    }

    private void Upgrade(UpgradeRequest request) {
      // Use the click-time snapshot, not the live EditorWindow fields.
      MjBody target = request.Target;
      int direction = request.Direction, railSide = request.RailSide;
      float travel = request.Travel, railOffset = request.RailOffset;
      float mass = request.Mass, damping = request.Damping, friction = request.Friction;
      ValidateTarget(target);
      if (!Finite(travel) || travel <= 0 || travel > 6 || !Finite(railOffset) || railOffset < 0.15f || railOffset > 1)
        throw new InvalidOperationException("Travel must be > 0 and <= 6 m. Rail offset must be 0.15..1.0 m.");
      if (!Finite(mass) || mass <= 0 || !Finite(damping) || damping < 0 || !Finite(friction) || friction < 0)
        throw new InvalidOperationException("Mass must be positive; damping and friction must be non-negative finite numbers.");
      string package = PackageRoot();
      string sourceXml = package + "/Models/door_slide.xml";
      if (!File.Exists(Absolute(sourceXml))) throw new FileNotFoundException("Missing door template: " + sourceXml);
      if (!EditorUtility.DisplayDialog("SDx Doors", "Upgrade ONLY:\n" + target.name +
          "\n\nThe frame stays at its current pose. The rigid leaf is shifted " + railOffset.ToString("F3") +
          " m to the selected rail side. The original is kept inactive.\n\nA scene backup will be saved first.", "Upgrade", "Cancel")) return;

      Scene scene = target.gameObject.scene;
      string backupFolder = package + "/Backups";
      EnsureFolder(backupFolder);
      string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
      string backup = AssetDatabase.GenerateUniqueAssetPath(backupFolder + "/" + SafeName(scene.name) + "_before_door_" + stamp + ".unity");
      if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Scene backup failed. No door was replaced.");
      AssetDatabase.Refresh();

      string prefix = "sdxdoor_" + Guid.NewGuid().ToString("N").Substring(0, 10) + "_";
      string generated = package + "/Generated/" + prefix.TrimEnd('_');
      EnsureFolder(generated);
      var doc = new XmlDocument();
      doc.Load(Absolute(sourceXml));
      foreach (XmlElement e in doc.SelectNodes("//*"))
        foreach (XmlAttribute a in e.Attributes)
          if (a.Value.Contains(Token)) a.Value = a.Value.Replace(Token, prefix);
      // Paths are relative to Generated/<id>/door.xml, not to a local user's home.
      foreach (XmlElement e in doc.SelectNodes("/mujoco/asset/mesh"))
        e.SetAttribute("file", "../../Models/" + e.GetAttribute("file"));
      foreach (XmlElement e in doc.SelectNodes("/mujoco/asset/texture"))
        e.SetAttribute("file", "../../Models/" + e.GetAttribute("file"));
      var leafNode = (XmlElement)doc.SelectSingleNode("/mujoco/worldbody/body[@name='" + prefix + "leaf']");
      var jointNode = (XmlElement)leafNode.SelectSingleNode("joint");
      leafNode.SetAttribute("pos", Num((railSide == 0 ? 1 : -1) * railOffset) + " 0 0");
      jointNode.SetAttribute("axis", direction == 0 ? "0 1 0" : "0 -1 0");
      jointNode.SetAttribute("range", "0 " + Num(travel));
      jointNode.SetAttribute("damping", Num(damping));
      jointNode.SetAttribute("frictionloss", Num(friction));
      var inertia = (XmlElement)leafNode.SelectSingleNode("inertial");
      float ratio = mass / 18f;
      inertia.SetAttribute("mass", Num(mass));
      inertia.SetAttribute("diaginertia", Num(14.9620761023 * ratio) + " " + Num(6.76902758424 * ratio) + " " + Num(8.19581160159 * ratio));
      string importPath = generated + "/" + prefix.TrimEnd('_') + ".xml";
      doc.Save(Absolute(importPath));
      AssetDatabase.ImportAsset(importPath, ImportAssetOptions.ForceSynchronousImport);

      GameObject created = null;
      int group;
      Undo.IncrementCurrentGroup();
      group = Undo.GetCurrentGroup();
      Undo.SetCurrentGroupName("Upgrade AWS door to passive sliding door");
      try {
        // The official importer compiles ONLY this small door model with the installed native library.
        created = new MjImporterWithAssets().ImportFile(Absolute(importPath));
        if (created == null) throw new InvalidOperationException("MuJoCo importer returned no root. Check the first Console error.");
        if (created.scene != scene) SceneManager.MoveGameObjectToScene(created, scene);
        created.name = target.name + "_Sliding";
        created.transform.SetParent(target.transform.parent, false);
        created.transform.localPosition = target.transform.localPosition;
        created.transform.localRotation = target.transform.localRotation;
        created.transform.localScale = Vector3.one;
        // Never keep global settings emitted by native canonicalization for a door-only import.
        foreach (var settings in created.GetComponentsInChildren<MjGlobalSettings>(true))
          UnityEngine.Object.DestroyImmediate(settings);

        MjBody frameBody = FindNamed<MjBody>(created, prefix + "frame");
        MjBody leafBody = FindNamed<MjBody>(created, prefix + "leaf");
        MjSlideJoint joint = FindNamed<MjSlideJoint>(created, prefix + "slide");
        var marker = created.AddComponent<SDxPassiveSlidingDoor>();
        marker.OriginalDoor = target.gameObject;
        marker.Frame = frameBody;
        marker.Leaf = leafBody;
        marker.Slide = joint;
        marker.FrontGrasp = FindTransform(created, prefix + "front_grasp");
        marker.BackGrasp = FindTransform(created, prefix + "back_grasp");
        marker.FrontApproach = FindTransform(created, prefix + "front_approach");
        marker.BackApproach = FindTransform(created, prefix + "back_approach");
        marker.ClosedLeafLocalPosition = leafBody.transform.localPosition;
        marker.LocalOpeningAxis = Vector3.forward * (direction == 0 ? 1 : -1);
        marker.Travel = travel;
        marker.InitialOpening = 0;
        marker.GeneratedMjcfPath = importPath;
        marker.BackupScenePath = backup;

        ApplyAppearance(created, target.gameObject, package);
        foreach (Transform t in created.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = target.gameObject.layer;
        if (created.GetComponentsInChildren<MjSlideJoint>(true).Length != 1 || created.GetComponentsInChildren<MjBody>(true).Length != 2)
          throw new InvalidOperationException("Unexpected imported structure. The original door was not deactivated.");
        Undo.RegisterCreatedObjectUndo(created, "Create passive sliding door");
        Undo.RecordObject(target.gameObject, "Deactivate original door");
        target.gameObject.SetActive(false);
        EditorUtility.SetDirty(target.gameObject);
        EditorUtility.SetDirty(marker);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(group);
      } catch {
        // Scene edits are confined to the new hierarchy and the original's active flag.
        Undo.RevertAllDownToGroup(group);
        if (target != null) target.gameObject.SetActive(true);
        if (created != null) UnityEngine.Object.DestroyImmediate(created);
        throw;
      }
      // Scene transaction is complete. UI feedback must not roll back a completed door
      // if a later Editor dialog/selection ends a GUI event.
      if (this.target == target) this.target = null;
      Selection.activeGameObject = created;
      EditorGUIUtility.PingObject(created);
      Debug.Log("[SDx Doors] Upgrade complete (" + Version + "). Original kept inactive. Scene backup: " + backup +
        ". This is a passive joint; no robot grasp controller is installed.", created);
      EditorUtility.DisplayDialog("SDx Doors", "Door upgraded. Save the scene with Ctrl+S.\n\nInspect the cyan travel guide before Play. " +
        "The robot will NOT grasp or open this door automatically yet.", "OK");
    }

    private static void ApplyAppearance(GameObject created, GameObject original, string package) {
      Material existing = original.GetComponentsInChildren<MeshRenderer>(true)
        .Where(r => r.sharedMaterial != null && r.sharedMaterial.mainTexture != null)
        .Select(r => r.sharedMaterial).FirstOrDefault();
      Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(package + "/Models/textures/aws_FoldingDoor_01.png");
      if (existing == null && tex == null) throw new FileNotFoundException("Door texture not found. Extract the entire SDxDoorInteraction folder.");
      foreach (MeshRenderer renderer in created.GetComponentsInChildren<MeshRenderer>(true)) {
        if (renderer.name.EndsWith("_frame_visual", StringComparison.Ordinal) || renderer.name.EndsWith("_leaf_visual", StringComparison.Ordinal)) {
          if (existing != null) renderer.sharedMaterial = existing;
          else {
            var mat = renderer.sharedMaterial;
            if (mat != null) {
              if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
              if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
              EditorUtility.SetDirty(mat);
            }
          }
          renderer.enabled = true;
        } else if (renderer.name.EndsWith("_collision", StringComparison.Ordinal)) renderer.enabled = false;
        else if (renderer.name.Contains("_handle_")) renderer.enabled = true;
      }
    }

    private static void ValidateTarget(MjBody body) {
      if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
      if (body == null || EditorUtility.IsPersistent(body) || !body.gameObject.scene.IsValid() || !body.gameObject.scene.isLoaded)
        throw new InvalidOperationException("Select the original FoldingDoor_01 MjBody in Hierarchy, not an asset or the whole world.");
      if (!IsOriginalName(body.name)) throw new InvalidOperationException("Supported original names end with FoldingDoor_01_001 or FoldingDoor_01_002.");
      if (!body.gameObject.activeInHierarchy) throw new InvalidOperationException("This original is inactive. It may already have a replacement. Do not upgrade it twice.");
      if (body.GetComponentInParent<SDxPassiveSlidingDoor>() != null) throw new InvalidOperationException("This is already an upgraded door.");
      if (body.GetComponentsInChildren<MjBody>(true).Length != 1 || body.GetComponentsInChildren<MjBaseJoint>(true).Length != 0)
        throw new InvalidOperationException("The original door hierarchy has extra bodies/joints. This tool will not overwrite custom articulation.");
      if (body.GetComponentsInChildren<Rigidbody>(true).Length != 0 || body.GetComponentsInChildren<ArticulationBody>(true).Length != 0)
        throw new InvalidOperationException("Remove/review mixed PhysX components before using the MuJoCo-only upgrade.");
      Vector3 scale = body.transform.lossyScale;
      if ((scale - Vector3.one).sqrMagnitude > 0.00001f)
        throw new InvalidOperationException("This patch expects original size (world scale 1,1,1). Do not apply it to a scaled/sheared door.");
      if (Vector3.Dot(body.transform.up, Vector3.up) < 0.999f)
        throw new InvalidOperationException("The door must be upright. A tilted sliding axis changes gravity behavior.");
      var b = new Bounds(); bool got = false;
      foreach (var filter in body.GetComponentsInChildren<MeshFilter>(true)) {
        if (filter.sharedMesh == null) continue;
        Bounds mb = filter.sharedMesh.bounds;
        for (int i = 0; i < 8; ++i) {
          Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
          Vector3 local = body.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
          if (!got) { b = new Bounds(local, Vector3.zero); got = true; } else b.Encapsulate(local);
        }
      }
      if (!got || Mathf.Abs(b.size.y - 2.21f) > 0.15f || Mathf.Abs(b.size.z - 2.50f) > 0.15f || b.size.x > 0.3f)
        throw new InvalidOperationException("Selected geometry does not match the supplied FoldingDoor_01 (about 2.50 m wide, 2.21 m high). No changes made.");
      Transform p = body.transform.parent;
      while (p != null) {
        if (p.GetComponent<MjComponent>() != null)
          throw new InvalidOperationException("The source door has a MuJoCo component ancestor. This patch expects a world-level door under plain grouping objects; review that custom hierarchy first.");
        p = p.parent;
      }
    }

    private static bool IsOriginalName(string s) { return s == SourcePrefix + "001" || s == SourcePrefix + "002"; }
    private static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
    private static string Num(double x) { return x.ToString("G12", CultureInfo.InvariantCulture); }
    private static string SafeName(string s) {
      foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
      return string.IsNullOrEmpty(s) ? "Scene" : s;
    }
    internal static string PackageRoot() {
      foreach (string guid in AssetDatabase.FindAssets("SDxDoorUpgradeWindow t:MonoScript")) {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        const string suffix = "/Editor/SDxDoorUpgradeWindow.cs";
        if (path.EndsWith(suffix, StringComparison.Ordinal)) return path.Substring(0, path.Length - suffix.Length);
      }
      throw new DirectoryNotFoundException("SDxDoorInteraction package root not found.");
    }
    internal static void EnsureFolder(string path) {
      string[] parts = path.Replace('\\', '/').Split('/');
      string current = parts[0];
      if (current != "Assets") throw new ArgumentException("Generated assets must stay under Assets.");
      for (int i = 1; i < parts.Length; i++) {
        string next = current + "/" + parts[i];
        if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
        current = next;
      }
    }
    internal static string Absolute(string assetPath) { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath)); }
    private static T FindNamed<T>(GameObject root, string name) where T : Component {
      T value = root.GetComponentsInChildren<T>(true).FirstOrDefault(x => x.name == name);
      if (value == null) throw new InvalidOperationException("Missing imported component: " + name);
      return value;
    }
    private static Transform FindTransform(GameObject root, string name) { return FindNamed<Transform>(root, name); }
  }
}
#endif
