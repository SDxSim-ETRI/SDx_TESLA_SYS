#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Optional editor utility for the CC0 MJCF furniture pack.
/// Does not change MjGeom collision, joint, or simulation settings.
/// Unity 2022.3 Built-in Render Pipeline / MuJoCo Unity Plugin.
/// </summary>
public static class FurnitureMJCFEditor {
  private const string TexturePath =
      "Assets/Models/furniture/Bookshelf/textures/wooden_display_shelves_01_diff_4k.jpg";
  private const string MaterialPath =
      "Assets/MuJoCoSetup/UnityMaterials/BookshelfWood.mat";

  [MenuItem("Tools/MJCF Furniture/Apply Shelf Texture to Selected Root")]
  private static void ApplyShelfTexture() {
    var root = Selection.activeGameObject;
    if (root == null) {
      EditorUtility.DisplayDialog("MJCF Furniture", "Select the imported shelf root in Hierarchy.", "OK");
      return;
    }

    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
    if (texture == null) {
      EditorUtility.DisplayDialog("MJCF Furniture",
          "Cannot find the diffuse texture at:\n" + TexturePath +
          "\n\nConfirm the JPG is in Assets/Models/furniture/Bookshelf/textures/ and visible in Unity.", "OK");
      return;
    }

    var shader = Shader.Find("Standard");
    if (shader == null) {
      EditorUtility.DisplayDialog("MJCF Furniture", "Standard shader not found. Check the Render Pipeline.", "OK");
      return;
    }

    Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    if (mat == null) {
      string parent = System.IO.Path.GetDirectoryName(MaterialPath).Replace('\\', '/');
      if (!AssetDatabase.IsValidFolder(parent)) {
        AssetDatabase.CreateFolder("Assets/MuJoCoSetup", "UnityMaterials");
      }
      mat = new Material(shader);
      mat.name = "BookshelfWood";
      AssetDatabase.CreateAsset(mat, MaterialPath);
    }
    Undo.RecordObject(mat, "Set MJCF bookshelf diffuse texture");
    mat.SetColor("_Color", Color.white);
    mat.SetTexture("_MainTex", texture);
    mat.SetFloat("_Glossiness", 0.27f);
    mat.SetFloat("_Metallic", 0f);
    EditorUtility.SetDirty(mat);

    int changed = 0;
    foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true)) {
      string name = renderer.gameObject.name.ToLowerInvariant();
      if (!(name.Contains("shelf") && name.EndsWith("_visual"))) continue;
      Undo.RecordObject(renderer, "Apply MJCF shelf wood texture");
      renderer.sharedMaterial = mat;
      EditorUtility.SetDirty(renderer);
      changed++;
    }
    AssetDatabase.SaveAssets();
    if (changed > 0 && root.scene.IsValid()) {
      EditorSceneManager.MarkSceneDirty(root.scene);
    }
    Debug.Log($"[MJCF Furniture] Applied wooden texture to {changed} shelf visual renderers on '{root.name}'.");
    if (changed == 0) {
      EditorUtility.DisplayDialog("MJCF Furniture",
          "No shelf visual geom was found under the selected object.\n" +
          "Select the imported MJCF root containing a furn_*_shelf_visual geom.", "OK");
    }
  }

  [MenuItem("Tools/MJCF Furniture/Hide Collision Renderers on Selected Root")]
  private static void HideCollisionRenderers() {
    var root = Selection.activeGameObject;
    if (root == null) {
      EditorUtility.DisplayDialog("MJCF Furniture", "Select the imported MJCF root in Hierarchy.", "OK");
      return;
    }
    int changed = 0;
    foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true)) {
      string name = renderer.gameObject.name.ToLowerInvariant();
      if (!name.Contains("_col_")) continue;
      if (!renderer.enabled) continue;
      Undo.RecordObject(renderer, "Hide MJCF collision mesh renderer");
      renderer.enabled = false;
      EditorUtility.SetDirty(renderer);
      changed++;
    }
    if (changed > 0 && root.scene.IsValid()) {
      EditorSceneManager.MarkSceneDirty(root.scene);
    }
    Debug.Log($"[MJCF Furniture] Hid {changed} collision renderers without disabling MjGeom.");
  }
}
#endif
