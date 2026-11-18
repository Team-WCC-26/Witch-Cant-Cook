#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CameraOcclusionSetup
{
    #region Editor

    /// <summary>Configures occlusion targets in the loaded MainScene.</summary>
    public static string ConfigureMainScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Configure camera occlusion outside Play mode.");

        Scene scene = SceneManager.GetSceneByPath("Assets/_Scenes/MainScene.unity");
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Open MainScene before configuring camera occlusion.");

        Transform map = FindTransform(scene, "NewMap");
        Transform cameraRoot = FindTransform(scene, "MainCamera");
        Transform followRoot = FindTransform(scene, "PlayerFollowCamera");
        Camera renderingCamera = cameraRoot != null ? cameraRoot.GetComponent<Camera>() : null;
        CinemachineCamera gameplayCamera = followRoot != null
            ? followRoot.GetComponent<CinemachineCamera>() : null;
        if (map == null || renderingCamera == null || gameplayCamera == null)
            throw new InvalidOperationException("MainScene requires NewMap, MainCamera and PlayerFollowCamera.");

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure Main Scene Camera Occlusion");

        var assignments = new Dictionary<Transform, List<Renderer>>();
        var assignedRenderers = new HashSet<Renderer>();
        var unsupportedRenderers = new HashSet<Renderer>();

        foreach (Transform child in map.GetComponentsInChildren<Transform>(true))
        {
            if (!IsGroupRoot(child, map)) continue;
            AddGroup(child, assignments, assignedRenderers, unsupportedRenderers);
        }

        foreach (Renderer renderer in map.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsMeshRenderer(renderer) || assignedRenderers.Contains(renderer)) continue;
            if (!IsIndividualTarget(renderer, map)) continue;
            AddRenderer(renderer.transform, renderer, assignments, assignedRenderers, unsupportedRenderers);
        }

        Transform standaloneTable = FindTransform(scene, "SFT_Table_004");
        if (standaloneTable != null && !standaloneTable.IsChildOf(map))
            AddGroup(standaloneTable, assignments, assignedRenderers, unsupportedRenderers);

        CameraOcclusionController controller = renderingCamera.GetComponent<CameraOcclusionController>();
        if (controller == null)
            controller = Undo.AddComponent<CameraOcclusionController>(renderingCamera.gameObject);
        var controllerData = new SerializedObject(controller);
        controllerData.FindProperty("gameplayCamera").objectReferenceValue = gameplayCamera;
        controllerData.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

        CinemachineThirdPersonFollow follow = gameplayCamera.GetComponent<CinemachineThirdPersonFollow>();
        if (follow != null)
        {
            Undo.RecordObject(follow, "Disable Gameplay Camera Obstacle Avoidance");
            follow.AvoidObstacles.Enabled = false;
            EditorUtility.SetDirty(follow);
            PrefabUtility.RecordPrefabInstancePropertyModifications(follow);
        }

        int changedOccluders = 0;
        int changedBatching = 0;
        int configuredRenderers = 0;
        var templates = new Dictionary<Material, Material>();
        foreach (KeyValuePair<Transform, List<Renderer>> assignment in assignments)
        {
            CameraOcclusionTarget target = assignment.Key.GetComponent<CameraOcclusionTarget>();
            if (target == null)
                target = Undo.AddComponent<CameraOcclusionTarget>(assignment.Key.gameObject);
            var targetData = new SerializedObject(target);
            SerializedProperty renderers = targetData.FindProperty("targetRenderers");
            renderers.arraySize = assignment.Value.Count;
            var targetSources = new HashSet<Material>();
            for (int index = 0; index < assignment.Value.Count; index++)
            {
                Renderer renderer = assignment.Value[index];
                renderers.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
                foreach (Material source in renderer.sharedMaterials)
                    if (IsSupportedMaterial(source)) targetSources.Add(source);
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                StaticEditorFlags excludedFlags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.BatchingStatic;
                if ((flags & excludedFlags) != 0)
                {
                    Undo.RecordObject(renderer.gameObject, "Configure Fading Renderer Static Flags");
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                        flags & ~excludedFlags);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.gameObject);
                    if ((flags & StaticEditorFlags.OccluderStatic) != 0) changedOccluders++;
                    if ((flags & StaticEditorFlags.BatchingStatic) != 0) changedBatching++;
                }
            }
            AssignMaterialTemplates(targetData, targetSources, templates);
            configuredRenderers += assignment.Value.Count;
            targetData.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);
        return $"Configured {assignments.Count} targets, {configuredRenderers} renderers; "
            + $"referenced {templates.Count} transparent material templates; "
            + $"removed Occluder Static from {changedOccluders} objects; "
            + $"excluded {changedBatching} objects from static batching; "
            + $"skipped {unsupportedRenderers.Count} unsupported renderers. "
            + "Rebake occlusion before validating visibility. Scene has not been saved.";
    }

    /// <summary>References transparent material variants for a configured target.</summary>
    public static void ConfigureTargetMaterials(CameraOcclusionTarget target)
    {
        if (target == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var targetData = new SerializedObject(target);
        var sources = new HashSet<Material>();
        SerializedProperty renderers = targetData.FindProperty("targetRenderers");
        for (int index = 0; index < renderers.arraySize; index++)
        {
            Renderer renderer = renderers.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
            if (renderer == null) continue;
            foreach (Material material in renderer.sharedMaterials)
                if (IsSupportedMaterial(material)) sources.Add(material);
        }
        AssignMaterialTemplates(targetData, sources, new Dictionary<Material, Material>());
        targetData.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static void AssignMaterialTemplates(SerializedObject targetData,
        HashSet<Material> sources, Dictionary<Material, Material> templates)
    {
        SerializedProperty materialTemplates = targetData.FindProperty("materialTemplates");
        materialTemplates.arraySize = sources.Count;
        int index = 0;
        foreach (Material source in sources)
        {
            if (!templates.TryGetValue(source, out Material template))
            {
                template = GetOrCreateTemplate(source);
                templates.Add(source, template);
            }
            SerializedProperty entry = materialTemplates.GetArrayElementAtIndex(index++);
            entry.FindPropertyRelative("source").objectReferenceValue = source;
            entry.FindPropertyRelative("transparent").objectReferenceValue = template;
        }
    }

    [MenuItem("Tools/Camera/Configure Main Scene Occlusion")]
    private static void ConfigureFromMenu()
    {
        Debug.Log(ConfigureMainScene());
    }

    private static Transform FindTransform(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == objectName) return child;
        return null;
    }

    private static bool IsGroupRoot(Transform candidate, Transform map)
    {
        Transform parent = candidate.parent;
        if (parent == null) return false;
        string objectName = candidate.name;
        if (parent == map.Find("CookingTools"))
            return objectName == "Pot" || objectName == "Oven_new"
                || objectName.StartsWith("PrepTable", StringComparison.Ordinal);
        if (parent == map.Find("Floor"))
            return objectName == "Stair" || objectName.StartsWith("Stair (", StringComparison.Ordinal);
        if (parent == map.Find("Doors"))
            return objectName == "KitchenDoor" || objectName == "LobbyDoor";
        if (parent != map.Find("Home") && parent != map.Find("Deco")
            && parent != map.Find("Shop") && parent != map.Find("Props")) return false;

        return objectName.StartsWith("SFI_Carpet_", StringComparison.Ordinal)
            || objectName.StartsWith("SFI_Curtain_", StringComparison.Ordinal)
            || objectName.StartsWith("SFI_Armchair", StringComparison.Ordinal)
            || objectName.StartsWith("SFI_Table_", StringComparison.Ordinal)
            || objectName.StartsWith("SFI_RTable_", StringComparison.Ordinal)
            || objectName.StartsWith("SFI_Shelf_", StringComparison.Ordinal)
            || objectName.StartsWith("SFT_Shelve_", StringComparison.Ordinal)
            || objectName.StartsWith("SFT_Bottle_Shelve", StringComparison.Ordinal)
            || objectName.StartsWith("SFV_Shelving_", StringComparison.Ordinal)
            || objectName.StartsWith("SFV_Bed_", StringComparison.Ordinal)
            || objectName.StartsWith("AWS_Reception_Shelf_", StringComparison.Ordinal)
            || objectName == "SFM_FS_Fabric_Stall_Anim"
            || objectName == "Scene_Prefab_2" || objectName == "Scene_Prefab_5"
            || objectName == "Scene_Prefab_6" || objectName == "Scene_Prefab_7"
            || objectName == "Scene_Prefab_13";
    }

    private static bool IsIndividualTarget(Renderer renderer, Transform map)
    {
        string layer = LayerMask.LayerToName(renderer.gameObject.layer);
        if (layer == "Wall" || layer == "Floor") return true;

        Transform home = map.Find("Home");
        if (home == null || !renderer.transform.IsChildOf(home)) return false;
        Transform ceiling = home.Find("Silling");
        return (ceiling != null && renderer.transform.IsChildOf(ceiling))
            || renderer.name.StartsWith("Silling_Window", StringComparison.Ordinal)
            || renderer.name.StartsWith("Wall_Siliing", StringComparison.Ordinal);
    }

    private static void AddGroup(Transform root, Dictionary<Transform, List<Renderer>> assignments,
        HashSet<Renderer> assignedRenderers, HashSet<Renderer> unsupportedRenderers)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsMeshRenderer(renderer)) continue;
            AddRenderer(root, renderer, assignments, assignedRenderers, unsupportedRenderers);
        }
    }

    private static void AddRenderer(Transform root, Renderer renderer,
        Dictionary<Transform, List<Renderer>> assignments, HashSet<Renderer> assignedRenderers,
        HashSet<Renderer> unsupportedRenderers)
    {
        if (assignedRenderers.Contains(renderer)) return;
        if (!HasSupportedMaterial(renderer))
        {
            unsupportedRenderers.Add(renderer);
            return;
        }
        if (!assignments.TryGetValue(root, out List<Renderer> renderers))
        {
            renderers = new List<Renderer>();
            assignments.Add(root, renderers);
        }
        renderers.Add(renderer);
        assignedRenderers.Add(renderer);
    }

    private static bool IsMeshRenderer(Renderer renderer)
    {
        if (!renderer.enabled || renderer.GetComponentInParent<Canvas>(true) != null) return false;
        if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh != null;
        return renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter)
            && filter.sharedMesh != null;
    }

    private static bool HasSupportedMaterial(Renderer renderer)
    {
        foreach (Material material in renderer.sharedMaterials)
            if (IsSupportedMaterial(material)) return true;
        return false;
    }

    private static bool IsSupportedMaterial(Material material)
    {
        return material != null && material.shader != null
            && material.shader.name == "Universal Render Pipeline/Lit"
            && material.HasProperty("_Surface") && material.GetFloat("_Surface") < 0.5f;
    }

    private static Material GetOrCreateTemplate(Material source)
    {
        const string folder = "Assets/Materials/CameraOcclusion";
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Materials", "CameraOcclusion");
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
            throw new InvalidOperationException($"Cannot identify source material {source.name}.");

        string materialName = source.name;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            materialName = materialName.Replace(invalid, '_');
        string path = $"{folder}/{materialName}_{guid}_{localId}.mat";
        Material template = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (template != null) return template;

        template = new Material(source) { name = source.name + " Camera Occlusion" };
        CameraOcclusionTarget.ConfigureTransparentMaterial(template);
        AssetDatabase.CreateAsset(template, path);
        Undo.RegisterCreatedObjectUndo(template, "Create Camera Occlusion Material Template");
        return template;
    }

    #endregion
}
#endif
