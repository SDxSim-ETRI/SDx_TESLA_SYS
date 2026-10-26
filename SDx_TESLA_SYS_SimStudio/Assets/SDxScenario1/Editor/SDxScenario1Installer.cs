using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mujoco;
using SDx.Scenario1;
using SDx.SimStudio.UI;
using SDx.DoorInteraction;

public static class SDxScenario1Installer {
  const string Root="Assets/SDxScenario1";
  // This is a one-time editor setup. When a controller already belongs to the
  // active scene, prohibit a second setup rather than resetting book/robot/doors.
  static SDxScenario1Controller[] ControllersIn(Scene scene) {
    if(!scene.IsValid() || !scene.isLoaded) return new SDxScenario1Controller[0];
    return scene.GetRootGameObjects()
      .SelectMany(g=>g.GetComponentsInChildren<SDxScenario1Controller>(true)).ToArray();
  }
  [MenuItem("Tools/SDx/Scenario 1/Prepare Current Scene", true)]
  static bool CanPrepare() {
    if(EditorApplication.isPlayingOrWillChangePlaymode) return false;
    Scene scene=SceneManager.GetActiveScene();
    return scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path) &&
      ControllersIn(scene).Length==0;
  }
  [MenuItem("Tools/SDx/Scenario 1/Prepare Current Scene")]
  public static void Prepare() {
    // Also protect direct calls that bypass the disabled menu item.
    Scene active=SceneManager.GetActiveScene();
    if(ControllersIn(active).Length!=0) {
      Debug.Log("[SDx OneTime] Scenario 1 is already prepared in the active scene. No changes made.");
      return;
    }
    if(EditorApplication.isPlayingOrWillChangePlaymode) {
      EditorUtility.DisplayDialog("Scenario 1","Stop Play before preparing the scene.","OK");return;
    }
    // Never change scenes/import assets while a GUILayout scope is being drawn.
    EditorApplication.delayCall += PrepareDeferred;
  }
  static GameObject Find(string target) {
    var all=SceneManager.GetActiveScene().GetRootGameObjects()
      .SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
    var matches=all.Where(t=>string.Equals(t.name.Replace(" ",""),target.Replace(" ",""),StringComparison.OrdinalIgnoreCase)).ToArray();
    if(matches.Length!=1)throw new InvalidOperationException("Expected one scene object named '"+target+"'; found "+matches.Length+".");
    return matches[0].gameObject;
  }
  static T Named<T>(GameObject root,string target)where T:Component {
    var matches=root.GetComponentsInChildren<T>(true).Where(c=>c.name==target).ToArray();
    if(matches.Length!=1)throw new InvalidOperationException("Missing/ambiguous "+typeof(T).Name+": "+target);
    return matches[0];
  }
  static void PrepareDeferred() {
    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
    try {
      Scene scene=SceneManager.GetActiveScene();
      if(!scene.IsValid()||string.IsNullOrEmpty(scene.path))throw new InvalidOperationException("Save the current MainScene first.");
      // Re-check on delayed execution. Never reset a prepared scene twice.
      var prepared=ControllersIn(scene);
      if(prepared.Length!=0) {
        if(prepared.Length==1) {
          Selection.activeGameObject=prepared[0].gameObject;
          Debug.Log("[SDx OneTime] Existing Scenario 1 selected; Prepare skipped without changing the scene.",prepared[0]);
        } else Debug.LogError("[SDx OneTime] Duplicate scenario controllers found; no setup was run.");
        return;
      }
      var robot=Find("dual_fr3_husky_unity390");var book=Find("book_01306 (1)");
      var shelf=Find("bookshelf_left486");var shelfRight=Find("bookshelf_right136");var booksRight=Find("Books_Right");
      var table=Find("aws_robomaker_residential_CoffeeTable_01_CoffeeTable_01_001");
      var ui=UnityEngine.Object.FindObjectsOfType<SDxToolsPanel>(true).Single(x=>x.gameObject.scene==scene);
      var doors=UnityEngine.Object.FindObjectsOfType<SDxPassiveSlidingDoor>(true).Where(x=>x.gameObject.scene==scene&&x.gameObject.activeInHierarchy).ToArray();
      if(doors.Length!=2)throw new InvalidOperationException("Expected the two upgraded sliding doors in this scene.");
      foreach(var d in doors)if(d.Leaf==null||d.Slide==null||d.OriginalDoor==null)throw new InvalidOperationException("Door references are incomplete.");
      if(Vector3.Distance(shelf.transform.position,new Vector3(5.321f,0,-4.5f))>.03f ||
         Quaternion.Angle(shelf.transform.rotation,Quaternion.Euler(0,90,0))>1)
        throw new InvalidOperationException("The calibrated left-shelf pose is (5.321,0,-4.5), Y=90 degrees. Current scene differs; this installer does not overwrite it.");
      if(Vector3.Distance(robot.transform.lossyScale,Vector3.one)>.001f || Vector3.Distance(book.transform.lossyScale,Vector3.one)>.001f)
        throw new InvalidOperationException("Robot and task-book scale must be (1,1,1).");
      // Validate all named components before changing the scene.
      var lj=Enumerable.Range(1,7).Select(i=>Named<MjHingeJoint>(robot,"left_fr3_joint"+i)).ToArray();
      var rj=Enumerable.Range(1,7).Select(i=>Named<MjHingeJoint>(robot,"right_fr3_joint"+i)).ToArray();
      var la=Enumerable.Range(1,7).Select(i=>Named<MjActuator>(robot,"left_fr3_joint"+i)).ToArray();
      var ra=Enumerable.Range(1,7).Select(i=>Named<MjActuator>(robot,"right_fr3_joint"+i)).ToArray();
      var wheels=new[]{"front_left_wheel_joint","front_right_wheel_joint","rear_left_wheel_joint","rear_right_wheel_joint"}.Select(n=>Named<MjActuator>(robot,n)).ToArray();
      var fingers=new[]{"left_fr3_finger_joint1","left_fr3_finger_joint2","right_fr3_finger_joint1","right_fr3_finger_joint2"}.Select(n=>Named<MjSlideJoint>(robot,n)).ToArray();
      var bookJoint=book.GetComponentsInChildren<MjFreeJoint>(true).Single();
      var bookBody=bookJoint.GetComponentInParent<MjBody>();
      var bookCollider=book.GetComponentsInChildren<MjGeom>(true).Single(x=>x.name.EndsWith("_col_box",StringComparison.Ordinal));
      var leftHand=Named<MjBody>(robot,"left_fr3_hand");
      var leftGrip=Named<MjActuator>(robot,"left_fr3_hand");var rightGrip=Named<MjActuator>(robot,"right_fr3_hand");
      var robotFree=robot.GetComponentsInChildren<MjFreeJoint>(true).Single();
      if(Math.Abs(Time.fixedDeltaTime-.001f)>1e-7)throw new InvalidOperationException("Keep Project Settings > Time > Fixed Timestep at 0.001.");
      if(!EditorUtility.DisplayDialog("Prepare Scenario 1",
        "A scene backup will be saved first.\n\nThis will position the robot and ONE existing book at the supplied start poses, open both doors, disable the right shelf/books and add the command controller. It will not reimport the world or edit the GUI.\n\nDuring Play, contact/gripper tuning is applied by the controller. The task still uses physical contact, not attachment.\n\nContinue?","Prepare","Cancel"))return;
      Directory.CreateDirectory(Root+"/Backups");
      string backup=Root+"/Backups/"+scene.name+"_before_scenario1_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity";
      if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Scene backup failed; scene was not modified.");
      int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Prepare Scenario 1");
      Undo.RecordObject(robot.transform,"Scenario 1 robot start");robot.transform.position=new Vector3(.581f,0,1.635f);robot.transform.rotation=Quaternion.identity;
      Undo.SetTransformParent(book.transform,null,"Move task book out of Books_Left");
      Undo.RecordObject(book.transform,"Scenario 1 book start");
      book.transform.SetPositionAndRotation(new Vector3(1.588f,.15111069f,1.644f),Quaternion.identity);
      Undo.RecordObject(shelfRight,"Disable right shelf");shelfRight.SetActive(false);
      Undo.RecordObject(booksRight,"Disable right books");booksRight.SetActive(false);
      foreach(var d in doors) {
        Undo.RecordObjects(new UnityEngine.Object[]{d,d.Leaf.transform,d.Slide,d.OriginalDoor},"Open scenario door");
        d.Leaf.transform.localPosition=d.ClosedLeafLocalPosition+d.LocalOpeningAxis.normalized*d.Travel;
        d.InitialOpening=d.Travel;d.Slide.Configuration=d.Travel;d.OriginalDoor.SetActive(false);
        EditorUtility.SetDirty(d);EditorUtility.SetDirty(d.Slide);
      }
      var controller=UnityEngine.Object.FindObjectsOfType<SDxScenario1Controller>(true).FirstOrDefault(x=>x.gameObject.scene==scene);
      if(controller==null) {
        var go=new GameObject("SDx_Scenario1");Undo.RegisterCreatedObjectUndo(go,"Create Scenario 1");
        controller=Undo.AddComponent<SDxScenario1Controller>(go);
      } else Undo.RecordObject(controller,"Update Scenario 1 references");
      controller.RobotRoot=robot.transform;controller.BookRoot=book.transform;controller.ShelfRoot=shelf.transform;
      controller.RobotFreeJoint=robotFree;controller.BookFreeJoint=bookJoint;controller.BookBody=bookBody;
      controller.BookCollision=bookCollider;controller.LeftHand=leftHand;
      controller.LeftJoints=lj;controller.RightJoints=rj;controller.LeftMotors=la;controller.RightMotors=ra;
      controller.WheelMotors=wheels;controller.Fingers=fingers;controller.DoorSlides=doors.Select(d=>d.Slide).ToArray();
      controller.LeftGripper=leftGrip;controller.RightGripper=rightGrip;
      var bridge=controller.GetComponent<SDxScenario1CommandBridge>();
      if(bridge==null)bridge=Undo.AddComponent<SDxScenario1CommandBridge>(controller.gameObject);
      else Undo.RecordObject(bridge,"Scenario 1 UI binding");
      bridge.ToolsUI=ui;bridge.Controller=controller;
      EditorUtility.SetDirty(controller);EditorUtility.SetDirty(bridge);
      Undo.CollapseUndoOperations(undo);EditorSceneManager.MarkSceneDirty(scene);Selection.activeGameObject=controller.gameObject;
      Debug.Log("[Scenario 1] Prepared. Backup: "+backup+". Save the scene. Play -> Send Command -> scenario command.",controller);
      EditorUtility.DisplayDialog("Scenario 1 prepared",
        "Save the scene (Ctrl+S).\n\nPress Play, open Send Command and enter the Korean scenario command followed by 1.\n\nThe command resets all dynamic objects and starts the task; sending it again restarts it.\n\nBackup: "+backup,"OK");
    }catch(Exception e){Debug.LogException(e);EditorUtility.DisplayDialog("Scenario 1 setup",e.Message,"OK");}
  }
  [MenuItem("Tools/SDx/Scenario 1/Show Setup Notes")]
  public static void Notes()=>EditorUtility.RevealInFinder(Path.GetFullPath(Root+"/README_KO.md"));
}
