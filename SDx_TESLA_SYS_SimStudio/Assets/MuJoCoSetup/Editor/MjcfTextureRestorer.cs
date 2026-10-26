#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Mujoco;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Standalone editor extension for MuJoCo Unity plugin 3.16.0 and Unity 2022.3.
// Put under Assets/MuJoCoSetup/Editor/ (or any Assets/Editor folder).
// The official 3.16.0 importer creates material assets but does not assign MJCF textures.
// This utility restores 2D texture bindings without modifying the MuJoCo package.
public static class MjcfTextureRestorer {
  private sealed class AssetReferences {
    public readonly Dictionary<string, string> TextureFiles =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public readonly Dictionary<string, string> MaterialTextures =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public readonly HashSet<string> ReadXmlFiles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  }

  // Avoid a second copy of the currently installed residential world.
  // This safeguard only concerns small_house.xml; other MJCF imports remain repeatable.
  private static bool SmallHouseAlreadyInCurrentScene(string xmlPath) {
    if (!string.Equals(Path.GetFileName(xmlPath), "small_house.xml", StringComparison.OrdinalIgnoreCase))
      return false;
    Scene scene = SceneManager.GetActiveScene();
    if (!scene.IsValid() || !scene.isLoaded) return false;
    return scene.GetRootGameObjects().Any(root => root.name.StartsWith("small_house", StringComparison.OrdinalIgnoreCase));
  }

  [MenuItem("Tools/MuJoCo/Import MJCF with Textures", false, 210)]
  private static void ImportWithTextures() {
    string xmlPath = SelectRootXml();
    if (string.IsNullOrEmpty(xmlPath)) return;
    if (SmallHouseAlreadyInCurrentScene(xmlPath)) {
      EditorUtility.DisplayDialog("MuJoCo", "The residential small_house world is already in this scene. " +
        "A second import would duplicate the house and collision geometry. " +
        "Use 'Restore Textures on Selected Import' for an existing world, or open a new scene for a separate world.", "OK");
      Debug.Log("[SDx OneTime] Duplicate small_house import skipped without scene changes.");
      return;
    }

    try {
      GameObject importedRoot = new MjImporterWithAssets().ImportFile(xmlPath);
      if (importedRoot == null) {
        EditorUtility.DisplayDialog("MuJoCo", "MJCF import did not return a scene root. Check the Console.", "OK");
        return;
      }
      Selection.activeGameObject = importedRoot;
      EditorGUIUtility.PingObject(importedRoot);
      Restore(xmlPath, importedRoot);
    } catch (Exception error) {
      Debug.LogException(error);
      EditorUtility.DisplayDialog("MuJoCo", "MJCF import or texture restoration failed. Check the Console.\n" + error.Message, "OK");
    }
  }

  [MenuItem("Tools/MuJoCo/Restore Textures on Selected Import", false, 211)]
  private static void RestoreExistingImport() {
    GameObject importedRoot = Selection.activeGameObject;
    if (importedRoot == null || EditorUtility.IsPersistent(importedRoot)) {
      EditorUtility.DisplayDialog("MuJoCo", "Select the imported MJCF root GameObject in Hierarchy first.", "OK");
      return;
    }

    string xmlPath = SelectRootXml();
    if (string.IsNullOrEmpty(xmlPath)) return;

    try {
      Restore(xmlPath, importedRoot);
    } catch (Exception error) {
      Debug.LogException(error);
      EditorUtility.DisplayDialog("MuJoCo", "Texture restoration failed. Check the Console.\n" + error.Message, "OK");
    }
  }

  private static string SelectRootXml() {
    return EditorUtility.OpenFilePanel(
        "Select the original root MJCF XML (for example, small_house.xml)",
        Application.dataPath, "xml");
  }

  private static void Restore(string sourceXmlPath, GameObject importedRoot) {
    string resourceFolder = FindImportedResourcesFolder(importedRoot);
    if (string.IsNullOrEmpty(resourceFolder)) {
      throw new DirectoryNotFoundException(
          "Could not locate this import's Resources folder under Assets/Local/MjImports. " +
          "Make sure you selected the imported root GameObject (not one of its children). " +
          "If you imported another way, try the 'Import MJCF with Textures' menu.");
    }

    var refs = new AssetReferences();
    ReadXmlAndIncludes(sourceXmlPath, refs);
    string importFolder = resourceFolder.Substring(
        0, resourceFolder.Length - "/Resources".Length);
    string texturesFolder = EnsureFolder(importFolder, "Textures");

    // Copy the source PNG files into the project so that Unity's AssetDatabase owns them.
    // Texture names (not the original filenames) are unique within a compiled MJCF model.
    var textureAssets = new Dictionary<string, string>(StringComparer.Ordinal);
    int missingSourceFiles = 0;
    foreach (string textureName in refs.MaterialTextures.Values.Distinct()) {
      string sourceTexture;
      if (!refs.TextureFiles.TryGetValue(textureName, out sourceTexture) ||
          !File.Exists(sourceTexture)) {
        Debug.LogWarning("[MuJoCo Texture Fix] Missing source texture: " + textureName);
        missingSourceFiles++;
        continue;
      }

      string extension = Path.GetExtension(sourceTexture).ToLowerInvariant();
      string assetPath = texturesFolder + "/" + MjEngineTool.Sanitize(textureName) + extension;
      string targetPath = MakeAbsoluteProjectPath(assetPath);
      if (!string.Equals(Path.GetFullPath(sourceTexture), Path.GetFullPath(targetPath),
                         StringComparison.OrdinalIgnoreCase)) {
        File.Copy(sourceTexture, targetPath, true);
      }
      textureAssets[textureName] = assetPath;
    }

    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

    int updatedMaterials = 0;
    int missingMaterials = 0;
    foreach (var materialMapping in refs.MaterialTextures) {
      string textureAssetPath;
      if (!textureAssets.TryGetValue(materialMapping.Value, out textureAssetPath)) continue;

      string materialName = MjEngineTool.Sanitize(materialMapping.Key);
      string materialPath = resourceFolder + "/" + materialName + ".mat";
      Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
      if (material == null) {
        // The MJCF may declare some materials which the stock importer did not create.
        Debug.LogWarning("[MuJoCo Texture Fix] Imported material not found: " + materialPath);
        missingMaterials++;
        continue;
      }

      Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureAssetPath);
      if (texture == null) {
        Debug.LogWarning("[MuJoCo Texture Fix] Unity could not load texture: " + textureAssetPath);
        missingSourceFiles++;
        continue;
      }

      Undo.RecordObject(material, "Bind MuJoCo texture");
      bool assigned = false;
      if (material.HasProperty("_MainTex")) {
        material.SetTexture("_MainTex", texture); // Built-in Standard shader
        assigned = true;
      }
      if (material.HasProperty("_BaseMap")) {
        material.SetTexture("_BaseMap", texture); // URP Lit shader
        assigned = true;
      }
      if (!assigned) {
        Debug.LogWarning("[MuJoCo Texture Fix] Material shader has no albedo texture slot: " + materialPath);
        continue;
      }
      EditorUtility.SetDirty(material);
      updatedMaterials++;
    }

    // Example: FloorB_01's visual OBJ has no 'vt' coordinates. A texture without
    // UVs samples a constant point. Clone such *visual* meshes with planar UVs.
    // Vertex positions and triangles are unchanged, so the MuJoCo geometry is unchanged.
    int generatedUvMeshes = GeneratePlanarUvsForTexturedVisualMeshes(importedRoot, importFolder);

    AssetDatabase.SaveAssets();
    Debug.Log(string.Format(
        "[MuJoCo Texture Fix] XML files: {0}, material-texture definitions: {1}, " +
        "materials updated: {2}, missing source textures: {3}, missing materials: {4}, " +
        "planar UV meshes generated: {5}. Imported root: {6}",
        refs.ReadXmlFiles.Count, refs.MaterialTextures.Count, updatedMaterials,
        missingSourceFiles, missingMaterials, generatedUvMeshes, importedRoot.name));
    EditorUtility.DisplayDialog(
        "MuJoCo texture restoration complete",
        "Updated materials: " + updatedMaterials + "\n" +
        "Generated UV meshes: " + generatedUvMeshes + "\n" +
        "Missing texture files: " + missingSourceFiles + "\n" +
        "Missing material assets: " + missingMaterials + "\n\n" +
        "Save the Unity scene after checking the result.", "OK");
  }

  private static string FindImportedResourcesFolder(GameObject root) {
    var foundFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true)) {
      if (renderer.sharedMaterial == null) continue;
      string materialPath = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
      if (string.IsNullOrEmpty(materialPath)) continue;
      materialPath = materialPath.Replace('\\', '/');
      if (!materialPath.StartsWith("Assets/Local/MjImports/", StringComparison.Ordinal) ||
          !materialPath.Contains("/Resources/")) continue;
      string dir = Path.GetDirectoryName(materialPath).Replace('\\', '/');
      if (dir.EndsWith("/Resources", StringComparison.Ordinal)) foundFolders.Add(dir);
    }

    if (foundFolders.Count == 1) return foundFolders.First();
    string expected = "Assets/Local/MjImports/" + root.name + "/Resources";
    return AssetDatabase.IsValidFolder(expected) ? expected : null;
  }

  private static string EnsureFolder(string parentFolder, string folderName) {
    string path = parentFolder + "/" + folderName;
    if (!AssetDatabase.IsValidFolder(path)) {
      string guid = AssetDatabase.CreateFolder(parentFolder, folderName);
      if (string.IsNullOrEmpty(guid)) {
        throw new IOException("Unable to create Unity asset folder: " + path);
      }
    }
    return path;
  }

  private static string MakeAbsoluteProjectPath(string assetPath) {
    if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal)) {
      throw new ArgumentException("Expected a path under Assets/: " + assetPath);
    }
    return Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
  }

  private static void ReadXmlAndIncludes(string xmlPath, AssetReferences refs) {
    string fullXmlPath = Path.GetFullPath(xmlPath);
    if (!refs.ReadXmlFiles.Add(fullXmlPath)) return;
    if (!File.Exists(fullXmlPath)) {
      throw new FileNotFoundException("MJCF include not found", fullXmlPath);
    }

    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
    var xml = new XmlDocument { XmlResolver = null };
    using (XmlReader reader = XmlReader.Create(fullXmlPath, settings)) xml.Load(reader);
    string xmlFolder = Path.GetDirectoryName(fullXmlPath);

    // In the supplied small-house world, texture file paths are relative to each
    // model.xml file. Check that first, then the optional compiler texturedir.
    string textureDir = string.Empty;
    XmlElement compiler = xml.SelectSingleNode("/mujoco/compiler") as XmlElement;
    if (compiler != null) textureDir = compiler.GetAttribute("texturedir");

    foreach (XmlNode node in xml.SelectNodes("/mujoco/asset/texture")) {
      XmlElement textureElement = node as XmlElement;
      if (textureElement == null) continue;
      string textureType = textureElement.GetAttribute("type");
      if (!string.IsNullOrEmpty(textureType) && textureType != "2d") continue;
      string name = textureElement.GetAttribute("name");
      string relative = textureElement.GetAttribute("file");
      if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relative)) continue;

      string localPath = Path.GetFullPath(Path.Combine(xmlFolder, relative));
      if (!File.Exists(localPath) && !string.IsNullOrEmpty(textureDir)) {
        localPath = Path.GetFullPath(Path.Combine(xmlFolder, textureDir, relative));
      }
      refs.TextureFiles[name] = localPath;
    }

    foreach (XmlNode node in xml.SelectNodes("/mujoco/asset/material")) {
      XmlElement materialElement = node as XmlElement;
      if (materialElement == null) continue;
      string materialName = materialElement.GetAttribute("name");
      string textureName = materialElement.GetAttribute("texture");
      if (!string.IsNullOrEmpty(materialName) && !string.IsNullOrEmpty(textureName)) {
        refs.MaterialTextures[materialName] = textureName;
      }
    }

    foreach (XmlNode node in xml.SelectNodes("/mujoco/include")) {
      XmlElement includeElement = node as XmlElement;
      if (includeElement == null) continue;
      string relative = includeElement.GetAttribute("file");
      if (string.IsNullOrEmpty(relative)) continue;
      ReadXmlAndIncludes(Path.Combine(xmlFolder, relative), refs);
    }
  }

  private static int GeneratePlanarUvsForTexturedVisualMeshes(GameObject root, string importFolder) {
    string uvFolder = null;
    int generated = 0;
    foreach (MjGeom geom in root.GetComponentsInChildren<MjGeom>(true)) {
      if (geom.ShapeType != MjShapeComponent.ShapeTypes.Mesh) continue;
      MeshRenderer renderer = geom.GetComponent<MeshRenderer>();
      if (renderer == null || !renderer.enabled || renderer.sharedMaterial == null ||
          renderer.sharedMaterial.mainTexture == null) continue;
      MjMeshShape shape = geom.Shape as MjMeshShape;
      if (shape == null || shape.Mesh == null || !shape.Mesh.isReadable) continue;

      Mesh original = shape.Mesh;
      if (original.vertexCount == 0 ||
          (original.uv != null && original.uv.Length == original.vertexCount)) continue;

      if (uvFolder == null) uvFolder = EnsureFolder(importFolder, "GeneratedUVs");
      Mesh fixedMesh = UnityEngine.Object.Instantiate(original);
      fixedMesh.name = original.name + "_planar_uv";
      Vector3[] vertices = fixedMesh.vertices;
      Bounds bounds = fixedMesh.bounds;
      Vector3 size = bounds.size;
      float xy = size.x * size.y;
      float xz = size.x * size.z;
      float yz = size.y * size.z;
      var uvs = new Vector2[vertices.Length];
      for (int i = 0; i < vertices.Length; i++) {
        Vector3 v = vertices[i] - bounds.min;
        if (xz >= xy && xz >= yz) {
          uvs[i] = new Vector2(v.x / Mathf.Max(size.x, 1e-6f),
                               v.z / Mathf.Max(size.z, 1e-6f));
        } else if (xy >= yz) {
          uvs[i] = new Vector2(v.x / Mathf.Max(size.x, 1e-6f),
                               v.y / Mathf.Max(size.y, 1e-6f));
        } else {
          uvs[i] = new Vector2(v.y / Mathf.Max(size.y, 1e-6f),
                               v.z / Mathf.Max(size.z, 1e-6f));
        }
      }
      fixedMesh.uv = uvs;
      fixedMesh.RecalculateTangents();

      string basePath = uvFolder + "/" + MjEngineTool.Sanitize(geom.name) + "_planar_uv.asset";
      string uniquePath = AssetDatabase.GenerateUniqueAssetPath(basePath);
      AssetDatabase.CreateAsset(fixedMesh, uniquePath);

      Undo.RecordObject(geom, "Generate UVs for MuJoCo visual mesh");
      shape.Mesh = fixedMesh;
      EditorUtility.SetDirty(geom);
      MeshFilter filter = geom.GetComponent<MeshFilter>();
      if (filter != null) {
        Undo.RecordObject(filter, "Generate UVs for MuJoCo visual mesh");
        filter.sharedMesh = fixedMesh;
        EditorUtility.SetDirty(filter);
      }
      generated++;
    }
    return generated;
  }
}
#endif
