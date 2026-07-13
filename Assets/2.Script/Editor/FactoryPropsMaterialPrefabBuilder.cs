using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryPropsMaterialPrefabBuilder
{
    private const string RootFolder = "Assets/3.Asset/Model/3D/factory-props";
    private const string SourceModelPath = RootFolder + "/source/Props_Factory.fbx";
    private const string TextureFolder = RootFolder + "/textures";
    private const string MaterialFolder = RootFolder + "/materials";
    private const string PrefabFolder = RootFolder + "/prefabs";
    private const string AutoRunMarkerPath = "Temp/RunFactoryPropsMaterialPrefabBuilder.flag";

    private static readonly string[] AlbedoSuffixes =
    {
        "_AlbedoTransparency",
        "_AlbedoTranspare",
        "_AlbedoTranspar",
        "_AlbedoTrans"
    };

    private static readonly string[] MetallicSuffixes =
    {
        "_MetallicSmoothness",
        "_MetallicSmoothn",
        "_MetallicSmooth",
        "_MetallicSmo"
    };

    static FactoryPropsMaterialPrefabBuilder()
    {
        if (!File.Exists(AutoRunMarkerPath))
            return;

        File.Delete(AutoRunMarkerPath);
        EditorApplication.delayCall += () => Build(false);
    }

    [MenuItem("Tools/Prototype005/Build Factory Props Prefabs")]
    public static void BuildFromMenu()
    {
        Build(false);
    }

    public static void BuildFromCommandLine()
    {
        bool success = Build(true);
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 1);
    }

    private static bool Build(bool commandLine)
    {
        try
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);

            AssetDatabase.Refresh();
            List<TextureSet> textureSets = BuildTextureSets();
            if (textureSets.Count == 0)
                throw new InvalidOperationException("No factory-props texture sets were found.");

            CreateOrUpdateMaterials(textureSets);
            CreatePrefabs(textureSets);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"FactoryPropsMaterialPrefabBuilder completed. Materials: {textureSets.Count}, output: {PrefabFolder}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("FactoryPropsMaterialPrefabBuilder failed:\n" + ex);
            if (!commandLine)
                EditorUtility.DisplayDialog("Factory Props Build Failed", ex.Message, "OK");
            return false;
        }
    }

    private static List<TextureSet> BuildTextureSets()
    {
        Dictionary<string, TextureSet> sets = new Dictionary<string, TextureSet>(StringComparer.OrdinalIgnoreCase);
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder });

        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            TextureKind kind;
            string baseName;

            if (!TryParseTextureName(name, out baseName, out kind))
                continue;

            ConfigureTextureImporter(path, kind);

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                continue;

            TextureSet set;
            if (!sets.TryGetValue(baseName, out set))
            {
                set = new TextureSet(baseName);
                sets.Add(baseName, set);
            }

            switch (kind)
            {
                case TextureKind.Albedo:
                    set.Albedo = texture;
                    break;
                case TextureKind.Metallic:
                    set.Metallic = texture;
                    break;
                case TextureKind.Normal:
                    set.Normal = texture;
                    break;
                case TextureKind.Emission:
                    set.Emission = texture;
                    break;
            }
        }

        return sets.Values.OrderBy(set => set.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryParseTextureName(string name, out string baseName, out TextureKind kind)
    {
        if (TryRemoveKnownSuffix(name, AlbedoSuffixes, out baseName))
        {
            kind = TextureKind.Albedo;
            return true;
        }

        if (TryRemoveKnownSuffix(name, MetallicSuffixes, out baseName))
        {
            kind = TextureKind.Metallic;
            return true;
        }

        if (name.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase))
        {
            baseName = name.Substring(0, name.Length - "_Normal".Length);
            kind = TextureKind.Normal;
            return true;
        }

        if (name.EndsWith("_Emission", StringComparison.OrdinalIgnoreCase))
        {
            baseName = name.Substring(0, name.Length - "_Emission".Length);
            kind = TextureKind.Emission;
            return true;
        }

        baseName = null;
        kind = TextureKind.Unknown;
        return false;
    }

    private static bool TryRemoveKnownSuffix(string name, string[] suffixes, out string baseName)
    {
        foreach (string suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                baseName = name.Substring(0, name.Length - suffix.Length);
                return true;
            }
        }

        baseName = null;
        return false;
    }

    private static void ConfigureTextureImporter(string path, TextureKind kind)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        bool changed = false;

        if (kind == TextureKind.Normal && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            changed = true;
        }
        else if (kind != TextureKind.Normal && importer.textureType == TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.Default;
            changed = true;
        }

        bool shouldUseSrgb = kind == TextureKind.Albedo || kind == TextureKind.Emission;
        if (importer.sRGBTexture != shouldUseSrgb)
        {
            importer.sRGBTexture = shouldUseSrgb;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();
    }

    private static void CreateOrUpdateMaterials(List<TextureSet> textureSets)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader == null)
            throw new InvalidOperationException("Neither URP Lit nor Standard shader could be found.");

        foreach (TextureSet set in textureSets)
        {
            string materialPath = $"{MaterialFolder}/{SanitizeFileName(set.DisplayName)}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.shader = shader;
            ApplyTexture(material, "_BaseMap", "_MainTex", set.Albedo);
            ApplyTexture(material, "_MetallicGlossMap", null, set.Metallic);
            ApplyTexture(material, "_BumpMap", null, set.Normal);
            ApplyTexture(material, "_EmissionMap", null, set.Emission);

            SetFloatIfExists(material, "_WorkflowMode", 1f);
            SetFloatIfExists(material, "_Metallic", set.Metallic == null ? 0f : 1f);
            SetFloatIfExists(material, "_Smoothness", set.Metallic == null ? 0.45f : 1f);
            SetFloatIfExists(material, "_BumpScale", set.Normal == null ? 0f : 1f);

            material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);

            SetKeyword(material, "_METALLICSPECGLOSSMAP", set.Metallic != null);
            SetKeyword(material, "_NORMALMAP", set.Normal != null);
            SetKeyword(material, "_EMISSION", set.Emission != null);

            if (set.Emission != null)
            {
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", Color.white);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }

            EditorUtility.SetDirty(material);
            set.Material = material;
        }
    }

    private static void CreatePrefabs(List<TextureSet> textureSets)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModelPath);
        if (model == null)
            throw new FileNotFoundException("Could not load source FBX.", SourceModelPath);

        ClearGeneratedPrefabs();

        GameObject root = PrefabUtility.InstantiatePrefab(model) as GameObject;
        if (root == null)
            root = UnityEngine.Object.Instantiate(model);

        root.name = "Props_Factory_Textured";

        try
        {
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
        catch
        {
            // If Unity imported the model as a plain object, there is nothing to unpack.
        }

        AssignmentReport report = AssignMaterials(root, textureSets);
        Debug.Log(report.ToLogString());

        string allPrefabPath = $"{PrefabFolder}/Props_Factory_Textured.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, allPrefabPath);

        int individualCount = 0;
        foreach (Transform child in root.transform)
        {
            if (child.GetComponentsInChildren<Renderer>(true).Length == 0)
                continue;

            GameObject clone = UnityEngine.Object.Instantiate(child.gameObject);
            clone.name = child.name;

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{PrefabFolder}/{SanitizeFileName(child.name)}.prefab");
            PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
            UnityEngine.Object.DestroyImmediate(clone);
            individualCount++;
        }

        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log($"Factory props prefabs saved. Combined: {allPrefabPath}, individual prefabs: {individualCount}");
    }

    private static void ClearGeneratedPrefabs()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(PrefabFolder + "/", StringComparison.OrdinalIgnoreCase))
                AssetDatabase.DeleteAsset(path);
        }
    }

    private static AssignmentReport AssignMaterials(GameObject root, List<TextureSet> textureSets)
    {
        AssignmentReport report = new AssignmentReport();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Length == 0)
                continue;

            string context = BuildRendererContext(renderer);

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                TextureSet best = FindBestSet(renderer, sharedMaterials[i], context, textureSets);
                if (best == null || best.Material == null)
                {
                    report.Unmatched.Add($"{renderer.name}[{i}]");
                    continue;
                }

                sharedMaterials[i] = best.Material;
                report.Add(best.DisplayName);
            }

            renderer.sharedMaterials = sharedMaterials;
            EditorUtility.SetDirty(renderer);
        }

        return report;
    }

    private static TextureSet FindBestSet(Renderer renderer, Material slotMaterial, string context, List<TextureSet> textureSets)
    {
        string topLevelName = GetTopLevelName(renderer.transform);
        string normalizedTopLevelName = Normalize(topLevelName);
        TextureSet explicitSet = FindExplicitSet(normalizedTopLevelName, textureSets);
        if (explicitSet != null)
            return explicitSet;

        string slotContext = context;
        if (slotMaterial != null)
            slotContext += " " + slotMaterial.name;

        string objectContext = topLevelName + " " + BuildTransformContext(renderer.transform);
        HashSet<string> objectTokens = Tokenize(objectContext);
        HashSet<string> slotTokens = Tokenize(slotContext);
        string normalizedContext = Normalize(context);
        TextureSet best = null;
        int bestScore = 0;

        foreach (TextureSet set in textureSets)
        {
            int score = 0;
            foreach (string token in set.MatchTokens)
            {
                if (objectTokens.Contains(token))
                    score += token.Length <= 2 ? 12 : 30;

                if (slotTokens.Contains(token))
                    score += token.Length <= 2 ? 3 : 8;
            }

            if (normalizedContext.Contains(set.NormalizedKey))
                score += 80;

            foreach (string compactPart in set.CompactParts)
            {
                if (compactPart.Length > 3 && normalizedContext.Contains(compactPart))
                    score += 18;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = set;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static string GetTopLevelName(Transform transform)
    {
        Transform current = transform;
        while (current.parent != null && current.parent.parent != null)
            current = current.parent;

        return current.name;
    }

    private static string BuildTransformContext(Transform transform)
    {
        List<string> parts = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        return string.Join(" ", parts);
    }

    private static TextureSet FindExplicitSet(string normalizedContext, List<TextureSet> textureSets)
    {
        if (normalizedContext.Contains("container02"))
            return FindSetEndingWith(textureSets, "Container_02");

        if (normalizedContext.Contains("container01"))
            return FindSetEndingWith(textureSets, "Container_01");

        if (normalizedContext.Contains("locker"))
            return FindSetEndingWith(textureSets, "Lockers");

        if (normalizedContext.Contains("wc02"))
            return FindSetEndingWith(textureSets, "WC_02");

        if (normalizedContext.Contains("wc01") || normalizedContext.Contains("toilet"))
            return FindSetEndingWith(textureSets, "WC_01");

        if (normalizedContext.Contains("sink") || normalizedContext.Contains("glass") || normalizedContext.Contains("bathroom"))
            return FindSetEndingWith(textureSets, "Bathroom");

        return null;
    }

    private static TextureSet FindSetEndingWith(List<TextureSet> textureSets, string suffix)
    {
        return textureSets.FirstOrDefault(set => set.DisplayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildRendererContext(Renderer renderer)
    {
        List<string> parts = new List<string> { renderer.name };

        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
            parts.Add(meshFilter.sharedMesh.name);

        SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
        if (skinned != null && skinned.sharedMesh != null)
            parts.Add(skinned.sharedMesh.name);

        Transform parent = renderer.transform.parent;
        while (parent != null)
        {
            parts.Add(parent.name);
            parent = parent.parent;
        }

        return string.Join(" ", parts);
    }

    private static HashSet<string> Tokenize(string value)
    {
        HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]+"))
        {
            string token = match.Value;
            if (token.Length == 0)
                continue;

            tokens.Add(token);
            if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase) && token.Length > 3)
                tokens.Add(token.Substring(0, token.Length - 1));
        }

        return tokens;
    }

    private static string Normalize(string value)
    {
        return Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", string.Empty);
    }

    private static void ApplyTexture(Material material, string primaryProperty, string fallbackProperty, Texture texture)
    {
        if (texture == null)
            return;

        if (material.HasProperty(primaryProperty))
            material.SetTexture(primaryProperty, texture);

        if (!string.IsNullOrEmpty(fallbackProperty) && material.HasProperty(fallbackProperty))
            material.SetTexture(fallbackProperty, texture);
    }

    private static void SetFloatIfExists(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder)?.Replace("\\", "/");
        string child = Path.GetFileName(folder);

        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
            throw new InvalidOperationException("Invalid folder path: " + folder);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, child);
    }

    private static string SanitizeFileName(string value)
    {
        string result = value;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            result = result.Replace(invalid, '_');

        result = result.Replace("+", "plus");
        result = Regex.Replace(result, "\\s+", "_");
        return result;
    }

    private enum TextureKind
    {
        Unknown,
        Albedo,
        Metallic,
        Normal,
        Emission
    }

    private sealed class TextureSet
    {
        public TextureSet(string key)
        {
            Key = key;
            DisplayName = key;
            NormalizedKey = Normalize(key);
            MatchTokens = Tokenize(key);
            CompactParts = key.Split(new[] { '_', '+', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(part => part.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string NormalizedKey { get; }
        public HashSet<string> MatchTokens { get; }
        public string[] CompactParts { get; }
        public Texture2D Albedo { get; set; }
        public Texture2D Metallic { get; set; }
        public Texture2D Normal { get; set; }
        public Texture2D Emission { get; set; }
        public Material Material { get; set; }
    }

    private sealed class AssignmentReport
    {
        private readonly Dictionary<string, int> assignments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public List<string> Unmatched { get; } = new List<string>();

        public void Add(string materialName)
        {
            int count;
            assignments.TryGetValue(materialName, out count);
            assignments[materialName] = count + 1;
        }

        public string ToLogString()
        {
            string assigned = string.Join(", ", assignments.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}").ToArray());
            string unmatched = Unmatched.Count == 0 ? "none" : string.Join(", ", Unmatched.ToArray());
            return $"Factory props material assignment. Assigned slots: {assigned}. Unmatched slots: {unmatched}";
        }
    }
}
