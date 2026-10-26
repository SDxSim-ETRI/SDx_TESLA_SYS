using System;
using System.Linq;
using SDx.Scenario1;
using SDx.DoorInteraction;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SDxScenario2Tools {
  [MenuItem("Tools/SDx/Scenario 1/Validate Current Setup")]
  public static void Validate() {
    // No layout-driven imports, no scene mutation, no second controller.
    EditorApplication.delayCall+=ValidateDeferred;
  }
  static void ValidateDeferred() {
    try {
      var scene=SceneManager.GetActiveScene();
      var controls=UnityEngine.Object.FindObjectsOfType<SDxScenario1Controller>(true)
        .Where(x=>x.gameObject.scene==scene && x.gameObject.activeInHierarchy).ToArray();
      if(controls.Length!=1)throw new InvalidOperationException("Use exactly one existing prepared SDx_Scenario1 controller in MainScene.");
      var c=controls[0];
      string detail;
      if(!c.ValidateDoorScenarioSetup(out detail))throw new InvalidOperationException(detail);
      var bridge=c.GetComponent<SDxScenario1CommandBridge>();
      if(bridge==null || bridge.ToolsUI==null || bridge.Controller!=c)
        throw new InvalidOperationException("Keep the existing SDxScenario1CommandBridge and ToolsUI references.");
      var doors=UnityEngine.Object.FindObjectsOfType<SDxPassiveSlidingDoor>(true)
        .Where(d=>d.gameObject.scene==scene && d.gameObject.activeInHierarchy).ToArray();
      if(doors.Length!=2)throw new InvalidOperationException("Exactly two upgraded passive doors must remain active.");
      foreach(var d in doors) {
        if(d.OriginalDoor==null || d.OriginalDoor.activeInHierarchy)
          throw new InvalidOperationException("The old fixed door must remain inactive: "+d.name);
        if(d.Leaf==null || d.Frame==null || d.Slide==null || d.FrontGrasp==null)
          throw new InvalidOperationException("Incomplete upgraded door: "+d.name);
        if(Math.Abs(d.Travel-2.4f)>.01)throw new InvalidOperationException("Keep the calibrated 2.4 m door travel.");
      }
      if(Math.Abs(Time.fixedDeltaTime-.001f)>1e-7)
        throw new InvalidOperationException("Keep Fixed Timestep at 0.001 seconds.");
      Selection.activeGameObject=c.gameObject;
      Debug.Log("[Scenario 1] SETUP CHECK PASSED. "+detail,c);
      EditorUtility.DisplayDialog("Scenario 1 ready",
        "Existing controller, UI and two passive doors are connected.\n\nNo scene was edited. Do NOT prepare or import the world again.\n\nPlay -> Send Command -> Korean scenario word followed by 1, or a supported natural-language command.\nThe former Scenario 2 command is disabled.\n\nThis check validates references, not a Unity physics run.","OK");
    } catch(Exception ex) {
      Debug.LogException(ex);
      EditorUtility.DisplayDialog("Scenario 1 setup check",ex.Message,"OK");
    }
  }
  [MenuItem("Tools/SDx/Scenario 1/Show Door Setup Notes")]
  public static void Notes() {
    EditorUtility.RevealInFinder(System.IO.Path.GetFullPath("Assets/SDxLLM/README_KO.md"));
  }
}
