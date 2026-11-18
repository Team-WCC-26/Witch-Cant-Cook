using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class OcclusionBakeOnPlay
{
    private static bool resumePlay;
    private static bool bakePending;
    private static bool enterPlayAfterBake;
    private static double dataDeadline;

    static OcclusionBakeOnPlay()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        if (resumePlay)
        {
            resumePlay = false;
            return;
        }

        EditorApplication.isPlaying = false;
        if (bakePending) return;
        bakePending = true;
        EditorApplication.update += BakeAndEnterPlayMode;
    }

    private static void BakeAndEnterPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= BakeAndEnterPlayMode;
        bakePending = false;
        StartBake(true);
    }

    #region Editor

    [MenuItem("Tools/Occlusion/Bake Loaded Scenes")]
    private static void BakeFromMenu()
    {
        StartBake(false);
    }

    private static void StartBake(bool enterPlay)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || StaticOcclusionCulling.isRunning)
        {
            Debug.LogError("[Occlusion] Bake blocked: Play mode or another bake is active.");
            return;
        }

        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene.isLoaded && string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[Occlusion] Save all loaded scenes before baking. Play was cancelled.");
                return;
            }
        }

        try
        {
            if (!EditorSceneManager.SaveOpenScenes()) return;
            StaticOcclusionCulling.Clear();
            if (!StaticOcclusionCulling.Compute())
            {
                Debug.LogError("[Occlusion] Bake failed or was cancelled. Play was cancelled.");
                return;
            }

            enterPlayAfterBake = enterPlay;
            bakePending = true;
            dataDeadline = EditorApplication.timeSinceStartup + 10d;
            EditorApplication.update += WaitForBake;
        }
        catch (Exception error)
        {
            Debug.LogException(error);
        }
    }

    private static void WaitForBake()
    {
        if (StaticOcclusionCulling.isRunning)
        {
            dataDeadline = EditorApplication.timeSinceStartup + 10d;
            return;
        }
        // Native bake startup and data publication can lag behind the running flag.
        if (StaticOcclusionCulling.umbraDataSize == 0 &&
            EditorApplication.timeSinceStartup < dataDeadline) return;
        EditorApplication.update -= WaitForBake;
        bakePending = false;
        try
        {
            if (StaticOcclusionCulling.umbraDataSize == 0)
            {
                Debug.LogError("[Occlusion] Bake produced no data. Play was cancelled.");
                return;
            }
            if (!EditorSceneManager.SaveOpenScenes()) return;
            AssetDatabase.SaveAssets();
            Debug.Log("[Occlusion] Bake completed and saved.");
            if (!enterPlayAfterBake) return;
            resumePlay = true;
            EditorApplication.isPlaying = true;
        }
        catch (Exception error)
        {
            Debug.LogException(error);
        }
        finally
        {
            enterPlayAfterBake = false;
        }
    }

    #endregion
}
