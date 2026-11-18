#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TestToolsBuild
{
    public const string Symbol = "WCC_TEST_TOOLS";
    private static bool testBuildInProgress;
    private static readonly Regex symbolPattern = new(@"(?<![A-Za-z0-9_])WCC_TEST_TOOLS(?![A-Za-z0-9_])");

    #region Editor

    /// <summary>Enables shared test tools for the Standalone player and Editor.</summary>
    [MenuItem("Tools/Test Tools/Enable WCC_TEST_TOOLS")]
    public static void EnableTestTools()
    {
        EnsureEditorReady();
        string[] defines = GetDefines(NamedBuildTarget.Standalone);
        if (!defines.Contains(Symbol))
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone,
                string.Join(";", defines.Concat(new[] { Symbol })));
        AssetDatabase.SaveAssets();
        Debug.Log("[Test Tools] WCC_TEST_TOOLS enabled for Standalone. Remove it before committing.");
    }

    /// <summary>Removes shared test symbols from platform and profile define lists.</summary>
    [MenuItem("Tools/Test Tools/Disable WCC_TEST_TOOLS")]
    public static void DisableTestTools()
    {
        EnsureEditorReady();
        foreach (NamedBuildTarget buildTarget in GetNamedTargets())
        {
            string[] defines = GetDefines(buildTarget);
            if (!defines.Contains(Symbol)) continue;
            PlayerSettings.SetScriptingDefineSymbols(buildTarget,
                string.Join(";", defines.Where(define => define != Symbol)));
        }
        foreach (BuildProfile profile in GetProfiles())
        {
            string[] defines = profile.scriptingDefines ?? Array.Empty<string>();
            if (!defines.Any(ContainsSymbol)) continue;
            Undo.RecordObject(profile, "Disable Shared Test Tools");
            profile.scriptingDefines = defines.Select(RemoveSymbol).Where(value => value.Length > 0).ToArray();
            EditorUtility.SetDirty(profile);
        }
        AssetDatabase.SaveAssets();
        ValidateBeforeCommit();
    }

    /// <summary>Checks all platform settings and build profiles for test symbols.</summary>
    public static bool ValidateBeforeCommit()
    {
        var locations = new List<string>();
        const string settingsPath = "ProjectSettings/ProjectSettings.asset";
        if (File.Exists(settingsPath) && ContainsSymbol(File.ReadAllText(settingsPath)))
            locations.Add(settingsPath);
        foreach (NamedBuildTarget buildTarget in GetNamedTargets())
            if (GetDefines(buildTarget).Contains(Symbol)) locations.Add("Player Settings: " + buildTarget.TargetName);
        foreach (BuildProfile profile in GetProfiles())
            if (ProfileContainsSymbol(profile)) locations.Add("Build Profile: " + AssetDatabase.GetAssetPath(profile));
        if (locations.Count == 0)
        {
            Debug.Log("[Test Tools] Commit check passed: WCC_TEST_TOOLS is absent from platform and profile settings.");
            return true;
        }
        Debug.LogWarning("[Test Tools] Remove WCC_TEST_TOOLS before committing:\n" + string.Join("\n", locations)
            + "\nUse Tools > Test Tools > Disable WCC_TEST_TOOLS. Profile-specific Player Settings overrides may require removal in the profile.");
        return false;
    }

    [MenuItem("Tools/Test Tools/Check Before Commit")]
    private static void CheckBeforeCommitFromMenu()
    {
        ValidateBeforeCommit();
    }

    /// <summary>Builds a Windows Development player with shared test tools.</summary>
    [MenuItem("Tools/Test Tools/Build Windows Test Player")]
    public static void BuildWindowsTestPlayer()
    {
        EnsureEditorReady();
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64
            && EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows)
            throw new InvalidOperationException("Switch the active build platform to Windows before creating a test player.");
        BuildProfile profile = BuildProfile.GetActiveBuildProfile();
        EditorBuildSettingsScene[] sceneSettings = profile != null
            ? profile.GetScenesForBuild() : EditorBuildSettings.scenes;
        string[] scenes = sceneSettings.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        if (scenes.Length == 0) throw new InvalidOperationException("Enable at least one scene in Build Profiles.");
        string path = EditorUtility.SaveFilePanel("Build Windows Test Player", "",
            "WitchCantCook-TestTools.exe", "exe");
        if (string.IsNullOrEmpty(path) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.Development | BuildOptions.ConnectWithProfiler,
            extraScriptingDefines = new[] { Symbol }
        };
        bool previousFrameTimingStats = PlayerSettings.enableFrameTimingStats;
        testBuildInProgress = true;
        try
        {
            PlayerSettings.enableFrameTimingStats = true;
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                Debug.LogError("[Test Tools] Test build result: " + report.summary.result);
            else
                Debug.Log("[Test Tools] Test player built: " + path);
        }
        finally
        {
            PlayerSettings.enableFrameTimingStats = previousFrameTimingStats;
            testBuildInProgress = false;
        }
    }

    internal static void ValidateBuild(BuildTarget target, string[] extraDefines = null)
    {
        if (testBuildInProgress) return;
        NamedBuildTarget namedTarget = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
        bool enabled = GetDefines(namedTarget).Contains(Symbol)
            || (extraDefines != null && extraDefines.Any(ContainsSymbol))
            || ProfileContainsSymbol(BuildProfile.GetActiveBuildProfile());
#if WCC_TEST_TOOLS
        enabled = true;
#endif
        if (enabled)
            throw new BuildFailedException("WCC_TEST_TOOLS is enabled. Use Tools > Test Tools > Build Windows Test Player "
                + "for an intentional test build, or disable WCC_TEST_TOOLS before building a normal player.");
    }

    private static void EnsureEditorReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Wait for Edit mode and script compilation before changing test tools or building.");
    }

    private static string[] GetDefines(NamedBuildTarget target)
    {
        try
        {
            return PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
        }
        catch (ArgumentException) when (target != NamedBuildTarget.Standalone)
        {
            // NamedBuildTarget retains removed platforms such as Stadia.
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<NamedBuildTarget> GetNamedTargets()
    {
        var names = new HashSet<string>();
        foreach (FieldInfo field in typeof(NamedBuildTarget).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(NamedBuildTarget)) continue;
            var target = (NamedBuildTarget)field.GetValue(null);
            if (target == NamedBuildTarget.Unknown || !names.Add(target.TargetName)) continue;
            yield return target;
        }
    }

    private static IEnumerable<BuildProfile> GetProfiles()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:BuildProfile"))
        {
            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(AssetDatabase.GUIDToAssetPath(guid));
            if (profile != null) yield return profile;
        }
    }

    private static bool ProfileContainsSymbol(BuildProfile profile)
    {
        if (profile == null) return false;
        if (profile.scriptingDefines != null && profile.scriptingDefines.Any(ContainsSymbol)) return true;
        string path = AssetDatabase.GetAssetPath(profile);
        return !string.IsNullOrEmpty(path) && File.Exists(path) && ContainsSymbol(File.ReadAllText(path));
    }

    private static bool ContainsSymbol(string value)
    {
        return !string.IsNullOrEmpty(value) && symbolPattern.IsMatch(value);
    }

    private static string RemoveSymbol(string value)
    {
        return string.Join(";", (value ?? string.Empty).Split(';').Where(define => define.Trim() != Symbol));
    }

    #endregion
}

public sealed class TestToolsBuildGuard : BuildPlayerProcessor, IPreprocessBuildWithReport
{
    #region Editor

    public override int callbackOrder => int.MinValue;

    /// <summary>Rejects unintended test symbols before player preparation.</summary>
    public override void PrepareForBuild(BuildPlayerContext context)
    {
        BuildPlayerOptions options = context.BuildPlayerOptions;
        TestToolsBuild.ValidateBuild(options.target, options.extraScriptingDefines);
    }

    /// <summary>Rejects test-enabled players outside the dedicated test build menu.</summary>
    public void OnPreprocessBuild(BuildReport report)
    {
        TestToolsBuild.ValidateBuild(report.summary.platform);
    }

    #endregion
}
#endif
