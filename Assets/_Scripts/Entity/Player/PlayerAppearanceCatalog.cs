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
    private static PlayerAppearanceCatalog catalog;

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
            bool enabled = (Next(ref state) & 1u) != 0;
            int index = (int)(Next(ref state) % (uint)part.variants.Length);
            if (!renderers.TryGetValue(part.name, out SkinnedMeshRenderer renderer)) continue;
            enabled = part.name == "Body" || (part.name == "Full" ? useFullBody : !useFullBody && enabled);
            renderer.gameObject.SetActive(enabled);
            if (!enabled)
            {
                renderer.sharedMesh = null;
                continue;
            }

            Variant variant = part.variants[index];
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
