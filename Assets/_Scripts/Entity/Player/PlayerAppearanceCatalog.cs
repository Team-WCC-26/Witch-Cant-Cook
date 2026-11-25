using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerAppearanceCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Variant
    {
        public Mesh mesh;
        public Material[] materials;
        public string[] boneNames;
        public Bounds bounds;
    }

    [Serializable]
    public sealed class Part
    {
        public string name;
        public Variant[] variants;
    }

    [SerializeField] private Part[] parts;
    [SerializeField] private List<Mesh> excludedMeshes = new List<Mesh>();
    private static PlayerAppearanceCatalog catalog;

    public bool IsRandomExcluded(Mesh mesh)
    {
        return excludedMeshes.Contains(mesh);
    }

    public static void Apply(PlayerBrain player, string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return;
        if (catalog == null) catalog = Resources.Load<PlayerAppearanceCatalog>("PlayerAppearanceCatalog");
        if (catalog == null)
        {
            Debug.LogError("Player appearance catalog is missing. Use Tools/Player/Rebuild Appearance Catalog.", player);
            return;
        }

        var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (Transform bone in player.Animator.GetComponentsInChildren<Transform>(true))
            bones.TryAdd(bone.name, bone);
        var renderers = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
        foreach (SkinnedMeshRenderer renderer in player.Animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            renderers.TryAdd(renderer.name.Split('_')[0], renderer);

        uint state = Hash(playerId);
        bool useFullBody = (Next(ref state) & 1u) != 0;
        foreach (Part part in catalog.parts)
        {
            int allowedCount = catalog.GetAllowedCount(part);
            if (part.name == "Body" && allowedCount == 0)
            {
                Debug.LogError("Allow at least one Body item in Random exclusions.", player);
                return;
            }
            if (part.name == "Full" && allowedCount == 0) useFullBody = false;
        }
        foreach (Part part in catalog.parts)
        {
            bool enabled = (Next(ref state) & 1u) != 0;
            uint variantSeed = Next(ref state);
            int allowedCount = catalog.GetAllowedCount(part);
            if (!renderers.TryGetValue(part.name, out SkinnedMeshRenderer renderer)) continue;
            enabled = part.name == "Body" || (part.name == "Full" ? useFullBody : !useFullBody && enabled);
            enabled &= allowedCount > 0;
            renderer.gameObject.SetActive(enabled);
            if (!enabled)
            {
                renderer.sharedMesh = null;
                continue;
            }

            int allowedIndex = (int)(variantSeed % (uint)allowedCount);
            Variant variant = null;
            foreach (Variant candidate in part.variants)
            {
                if (catalog.IsRandomExcluded(candidate.mesh)) continue;
                if (allowedIndex-- != 0) continue;
                variant = candidate;
                break;
            }
            var mappedBones = new Transform[variant.boneNames.Length];
            for (int boneIndex = 0; boneIndex < mappedBones.Length; boneIndex++)
            {
                if (!bones.TryGetValue(variant.boneNames[boneIndex], out mappedBones[boneIndex]))
                {
                    Debug.LogError($"Appearance bone missing: {variant.boneNames[boneIndex]}", player);
                    return;
                }
            }
            renderer.sharedMesh = variant.mesh;
            renderer.enabled = true;
            renderer.sharedMaterials = variant.materials;
            renderer.bones = mappedBones;
            renderer.localBounds = variant.bounds;
        }
    }

    private int GetAllowedCount(Part part)
    {
        int count = 0;
        foreach (Variant variant in part.variants)
        {
            if (!IsRandomExcluded(variant.mesh)) count++;
        }
        return count;
    }

    private static uint Hash(string value)
    {
        // FNV-1a over UTF-16 code units; stable across processes and platforms.
        uint hash = 2166136261u;
        foreach (char character in value)
            hash = unchecked((hash ^ character) * 16777619u);
        return hash;
    }

    private static uint Next(ref uint state)
    {
        // Local PRNG; never changes Unity's global random state.
        state = unchecked(state * 1664525u + 1013904223u);
        return state >> 8;
    }

#if UNITY_EDITOR
    #region Editor

    [UnityEditor.InitializeOnLoadMethod]
    private static void EnsureCatalog()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(CatalogPath) == null)
                RebuildCatalog();
        };
    }

    private const string CatalogPath = "Assets/Resources/PlayerAppearanceCatalog.asset";

    public static PlayerAppearanceCatalog GetEditorCatalog()
    {
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(CatalogPath);
        if (asset == null)
        {
            RebuildCatalog();
            asset = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(CatalogPath);
        }
        return asset;
    }

    public void SetRandomExcluded(Mesh mesh, bool excluded)
    {
        UnityEditor.Undo.RecordObject(this, "Change random appearance exclusion");
        if (excluded)
        {
            if (!excludedMeshes.Contains(mesh)) excludedMeshes.Add(mesh);
        }
        else
        {
            excludedMeshes.Remove(mesh);
        }
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
    }

    public void ImportLegacyExclusions(HashSet<string> legacyKeys)
    {
        foreach (Part part in parts)
        {
            foreach (Variant variant in part.variants)
            {
                UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(variant.mesh, out string assetGuid, out long localId);
                if (legacyKeys.Contains($"{assetGuid}:{localId}")) SetRandomExcluded(variant.mesh, true);
            }
        }
    }

    [UnityEditor.MenuItem("Tools/Player/Rebuild Appearance Catalog")]
    public static void RebuildCatalog()
    {
        const string meshRoot = "Assets/ExternalAssets/CharactersAsset/Funny_Characters/Meshes";
        string[] paths = Array.ConvertAll(UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { meshRoot }),
            UnityEditor.AssetDatabase.GUIDToAssetPath);
        Array.Sort(paths, StringComparer.Ordinal);
        var groups = new SortedDictionary<string, List<Variant>>(StringComparer.Ordinal);
        var partNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Hair", "Glasses", "Outerwear", "Hat", "Body", "Pants",
            "Mustache", "Glove", "Shoe", "Eyebrow", "Backpack", "Full"
        };
        foreach (string path in paths)
        {
            if (path == meshRoot + "/Basic_Character.fbx") continue;
            GameObject model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var modelRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Array.Sort(modelRenderers, (left, right) => StringComparer.Ordinal.Compare(left.name, right.name));
            foreach (SkinnedMeshRenderer renderer in modelRenderers)
            {
                if (renderer.sharedMesh == null) continue;
                string partName = path.StartsWith(meshRoot + "/Full_Body/", StringComparison.Ordinal)
                    ? "Full" : renderer.name.Split('_')[0];
                if (!partNames.Contains(partName)) continue;
                if (!groups.TryGetValue(partName, out List<Variant> variants))
                    groups.Add(partName, variants = new List<Variant>());
                variants.Add(new Variant
                {
                    mesh = renderer.sharedMesh,
                    materials = renderer.sharedMaterials,
                    boneNames = Array.ConvertAll(renderer.bones, bone => bone.name),
                    bounds = renderer.localBounds
                });
            }
        }

        var entries = new List<Part>();
        foreach (var group in groups)
            entries.Add(new Part { name = group.Key, variants = group.Value.ToArray() });
        if (entries.Count != 12) throw new InvalidOperationException($"Expected 12 appearance parts, found {entries.Count}.");
        if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Resources"))
            UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources");
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(CatalogPath);
        if (asset == null)
        {
            asset = CreateInstance<PlayerAppearanceCatalog>();
            UnityEditor.AssetDatabase.CreateAsset(asset, CatalogPath);
        }
        asset.parts = entries.ToArray();
        UnityEditor.EditorUtility.SetDirty(asset);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"Player appearance catalog saved: {entries.Count} parts.");
    }

    #endregion
#endif
}
