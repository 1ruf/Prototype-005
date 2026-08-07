using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WorldGridStyleBatchApplier
{
    private const string ShaderName = "Prototype 014/World Grid Style Lit";
    private const string GeneratedMaterialFolder = "Assets/WorldGridStyle/GeneratedMaterials";

    public static void ApplyAll()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError("월드 격자 스타일 셰이더를 찾을 수 없습니다.");
            return;
        }

        EnsureFolder("Assets/WorldGridStyle");
        EnsureFolder(GeneratedMaterialFolder);

        var materialCache = new Dictionary<Material, Material>();
        int rendererCount = 0;
        int materialSlotCount = 0;

        foreach (string prefabGuid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            bool changed = ApplyToRenderers(prefabRoot.GetComponentsInChildren<Renderer>(true), shader, materialCache, ref rendererCount, ref materialSlotCount);
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        foreach (string sceneGuid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuid);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            bool changed = ApplyToRenderers(Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None), shader, materialCache, ref rendererCount, ref materialSlotCount);
            if (changed)
            {
                EditorSceneManager.SaveScene(scene);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"월드 격자 스타일 적용 완료: Renderer {rendererCount}개, Material Slot {materialSlotCount}개");
    }

    private static bool ApplyToRenderers(IEnumerable<Renderer> renderers, Shader shader, Dictionary<Material, Material> materialCache, ref int rendererCount, ref int materialSlotCount)
    {
        bool anyChanged = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            {
                continue;
            }

            Material[] sourceMaterials = renderer.sharedMaterials;
            bool rendererChanged = false;
            for (int index = 0; index < sourceMaterials.Length; index++)
            {
                Material sourceMaterial = sourceMaterials[index];
                if (sourceMaterial == null || sourceMaterial.shader == shader || IsTransparent(sourceMaterial))
                {
                    continue;
                }

                sourceMaterials[index] = GetOrCreateStyleMaterial(sourceMaterial, shader, materialCache);
                rendererChanged = true;
                materialSlotCount++;
            }

            if (rendererChanged)
            {
                renderer.sharedMaterials = sourceMaterials;
                EditorUtility.SetDirty(renderer);
                rendererCount++;
                anyChanged = true;
            }
        }
        return anyChanged;
    }

    private static Material GetOrCreateStyleMaterial(Material sourceMaterial, Shader shader, Dictionary<Material, Material> materialCache)
    {
        if (materialCache.TryGetValue(sourceMaterial, out Material styleMaterial))
        {
            return styleMaterial;
        }

        string sourcePath = AssetDatabase.GetAssetPath(sourceMaterial);
        string sourceKey = string.IsNullOrEmpty(sourcePath) ? sourceMaterial.name : sourcePath + sourceMaterial.name;
        string materialPath = GeneratedMaterialFolder + "/WorldGrid_" + Hash128.Compute(sourceKey) + ".mat";
        styleMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (styleMaterial == null)
        {
            styleMaterial = new Material(shader);
            Texture baseMap = GetBaseMap(sourceMaterial);
            styleMaterial.SetTexture("_BaseMap", baseMap);
            styleMaterial.SetColor("_BaseColor", GetBaseColor(sourceMaterial));
            styleMaterial.SetFloat("_TextureMapping", baseMap == null ? 0f : 1f);
            styleMaterial.enableInstancing = sourceMaterial.enableInstancing;
            if (baseMap != null)
            {
                styleMaterial.SetTextureScale("_BaseMap", GetBaseMapScale(sourceMaterial));
                styleMaterial.SetTextureOffset("_BaseMap", GetBaseMapOffset(sourceMaterial));
            }
            AssetDatabase.CreateAsset(styleMaterial, materialPath);
        }

        materialCache[sourceMaterial] = styleMaterial;
        return styleMaterial;
    }

    private static Texture GetBaseMap(Material material)
    {
        if (material.HasProperty("_BaseMap"))
        {
            return material.GetTexture("_BaseMap");
        }
        return material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
    }

    private static Color GetBaseColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }
        return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
    }

    private static Vector2 GetBaseMapScale(Material material)
    {
        return material.HasProperty("_BaseMap") ? material.GetTextureScale("_BaseMap") : material.GetTextureScale("_MainTex");
    }

    private static Vector2 GetBaseMapOffset(Material material)
    {
        return material.HasProperty("_BaseMap") ? material.GetTextureOffset("_BaseMap") : material.GetTextureOffset("_MainTex");
    }

    private static bool IsTransparent(Material material)
    {
        return material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent || (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f);
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        int slashIndex = folderPath.LastIndexOf('/');
        EnsureFolder(folderPath[..slashIndex]);
        AssetDatabase.CreateFolder(folderPath[..slashIndex], folderPath[(slashIndex + 1)..]);
    }
}
