using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEngine;

public class MJCFXmlTextureMapper : EditorWindow
{
    private TextAsset mjcfXml;
    private DefaultAsset importedResourcesFolder;
    private DefaultAsset texturesFolder;

    [MenuItem("Tools/MJCF/Map Textures From MJCF XML")]
    public static void ShowWindow()
    {
        GetWindow<MJCFXmlTextureMapper>("MJCF XML Texture Mapper");
    }

    private void OnGUI()
    {
        GUILayout.Label("MJCF XML Texture Mapper", EditorStyles.boldLabel);

        mjcfXml = (TextAsset)EditorGUILayout.ObjectField(
            "MJCF XML File",
            mjcfXml,
            typeof(TextAsset),
            false
        );

        importedResourcesFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Imported Resources Folder",
            importedResourcesFolder,
            typeof(DefaultAsset),
            false
        );

        texturesFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Textures Folder",
            texturesFolder,
            typeof(DefaultAsset),
            false
        );

        GUILayout.Space(10);

        if (GUILayout.Button("Map Textures From XML"))
        {
            MapTextures();
        }
    }

    private void MapTextures()
    {
        if (mjcfXml == null || importedResourcesFolder == null || texturesFolder == null)
        {
            Debug.LogError("MJCF XML, Imported Resources Folder, Textures Folder를 모두 넣어줘.");
            return;
        }

        string xmlPath = AssetDatabase.GetAssetPath(mjcfXml);
        string resourcesPath = AssetDatabase.GetAssetPath(importedResourcesFolder);
        string texturesPath = AssetDatabase.GetAssetPath(texturesFolder);

        Dictionary<string, string> textureNameToRelativePath = new Dictionary<string, string>();
        Dictionary<string, string> materialNameToTexturePath = new Dictionary<string, string>();

        XmlDocument doc = new XmlDocument();
        doc.Load(xmlPath);

        XmlNodeList textureNodes = doc.GetElementsByTagName("texture");

        foreach (XmlNode node in textureNodes)
        {
            XmlAttribute nameAttr = node.Attributes?["name"];
            XmlAttribute fileAttr = node.Attributes?["file"];

            if (nameAttr == null || fileAttr == null)
                continue;

            string texName = nameAttr.Value;
            string texPath = NormalizePath(fileAttr.Value);

            textureNameToRelativePath[texName] = texPath;
        }

        XmlNodeList materialNodes = doc.GetElementsByTagName("material");

        foreach (XmlNode node in materialNodes)
        {
            XmlAttribute nameAttr = node.Attributes?["name"];
            XmlAttribute textureAttr = node.Attributes?["texture"];

            if (nameAttr == null || textureAttr == null)
                continue;

            string matName = nameAttr.Value;
            string texName = textureAttr.Value;

            if (textureNameToRelativePath.TryGetValue(texName, out string texPath))
            {
                materialNameToTexturePath[matName] = texPath;
            }
        }

        string[] unityMaterialGuids = AssetDatabase.FindAssets("t:Material", new[] { resourcesPath });
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { texturesPath });

        int mapped = 0;
        int failed = 0;

        foreach (string matGuid in unityMaterialGuids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(matGuid);
            Material unityMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (unityMat == null)
                continue;

            string unityMatName = unityMat.name;

            string matchedMjcfMaterial = null;
            string matchedTexturePath = null;

            foreach (var pair in materialNameToTexturePath)
            {
                string mjcfMatName = pair.Key;

                if (IsMaterialNameMatch(unityMatName, mjcfMatName))
                {
                    matchedMjcfMaterial = mjcfMatName;
                    matchedTexturePath = pair.Value;
                    break;
                }
            }

            if (matchedTexturePath == null)
            {
                failed++;
                Debug.LogWarning($"MJCF material 매칭 실패: {unityMatName}");
                continue;
            }

            Texture2D texture = FindTextureByRelativePath(textureGuids, matchedTexturePath);

            if (texture == null)
            {
                failed++;
                Debug.LogWarning($"Texture 파일 못 찾음: {unityMatName} / MJCF:{matchedMjcfMaterial} -> {matchedTexturePath}");
                continue;
            }

            ApplyTexture(unityMat, texture);
            EditorUtility.SetDirty(unityMat);

            mapped++;
            Debug.Log($"Mapped: {unityMatName} / MJCF:{matchedMjcfMaterial} -> {matchedTexturePath}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"완료: {mapped}개 매핑 성공, {failed}개 매칭 실패");
    }

    private bool IsMaterialNameMatch(string unityMatName, string mjcfMatName)
    {
        if (string.IsNullOrEmpty(unityMatName) || string.IsNullOrEmpty(mjcfMatName))
            return false;

        if (unityMatName.Equals(mjcfMatName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (unityMatName.IndexOf(mjcfMatName, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Unity MuJoCo Plugin이 material 이름 뒤에 suffix를 붙이는 경우 대응
        // 예: objectmaterial_0-0, objectmaterial_0-1
        string cleanedUnityName = unityMatName.Replace("_", "").Replace("-", "").ToLowerInvariant();
        string cleanedMjcfName = mjcfMatName.Replace("_", "").Replace("-", "").ToLowerInvariant();

        if (cleanedUnityName.Contains(cleanedMjcfName))
            return true;

        return false;
    }

    private Texture2D FindTextureByRelativePath(string[] textureGuids, string mjcfTexturePath)
    {
        mjcfTexturePath = NormalizePath(mjcfTexturePath);

        string mjcfFileName = Path.GetFileName(mjcfTexturePath);

        foreach (string guid in textureGuids)
        {
            string unityPath = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));

            // 1순위: MJCF의 상대경로 끝부분과 Unity asset 경로가 일치하는 경우
            // 예:
            // MJCF: textures/002_master_chef_can/texture_map.png
            // Unity: Assets/Models/generated_scene_output/textures/002_master_chef_can/texture_map.png
            if (unityPath.EndsWith(mjcfTexturePath, StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<Texture2D>(unityPath);
            }

            // 2순위: MJCF 경로가 textures/로 시작하지 않을 때 대비
            string withoutTexturesPrefix = RemoveTexturesPrefix(mjcfTexturePath);

            if (!string.IsNullOrEmpty(withoutTexturesPrefix) &&
                unityPath.EndsWith(withoutTexturesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<Texture2D>(unityPath);
            }
        }

        // 3순위 fallback:
        // texture_map.png는 여러 YCB 폴더에 중복되므로 파일명 매칭 금지
        if (!mjcfFileName.Equals("texture_map.png", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string guid in textureGuids)
            {
                string unityPath = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));

                if (Path.GetFileName(unityPath).Equals(mjcfFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(unityPath);
                }
            }
        }

        return null;
    }

    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        return path.Replace("\\", "/").Trim();
    }

    private string RemoveTexturesPrefix(string path)
    {
        path = NormalizePath(path);

        if (path.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring("textures/".Length);
        }

        return path;
    }

    private void ApplyTexture(Material mat, Texture2D texture)
    {
        // Built-in Standard Shader
        if (mat.HasProperty("_MainTex"))
        {
            mat.SetTexture("_MainTex", texture);
        }

        // URP Lit Shader
        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", texture);
        }

        // HDRP Lit Shader
        if (mat.HasProperty("_BaseColorMap"))
        {
            mat.SetTexture("_BaseColorMap", texture);
        }

        // 색상 tint 초기화
        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", Color.white);
        }

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", Color.white);
        }
    }
}