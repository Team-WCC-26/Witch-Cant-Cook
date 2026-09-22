using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public enum MotionValidationSeverity
{
    Warning,
    Error
}

public readonly struct MotionValidationIssue
{
    public readonly MotionValidationSeverity Severity;
    public readonly string Message;

    public MotionValidationIssue(MotionValidationSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
    }
}

public static class HumanoidAnimationClipBuilder
{
    private static readonly HashSet<string> ValidMuscleNames =
        new(HumanTrait.MuscleName, StringComparer.Ordinal);

    public static IReadOnlyList<MotionValidationIssue> Validate(
        HumanoidMotionDefinition motion)
    {
        List<MotionValidationIssue> issues = new();

        if (motion == null)
        {
            AddError(issues, "Motion definition is missing.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(motion.DisplayName))
            AddError(issues, "Display name is empty.");

        if (string.IsNullOrWhiteSpace(motion.ClipName)
            || motion.ClipName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            AddError(issues, "Clip name is empty or contains invalid characters.");
        }

        if (string.IsNullOrWhiteSpace(motion.OutputFolder)
            || !motion.OutputFolder.StartsWith("Assets/", StringComparison.Ordinal)
            || motion.OutputFolder.EndsWith("/", StringComparison.Ordinal))
        {
            AddError(issues, "Output folder must be an Assets/... path without a trailing slash.");
        }

        if (!IsFinite(motion.Duration) || motion.Duration <= 0f)
            AddError(issues, "Duration must be a finite value greater than zero.");

        if (!IsFinite(motion.FrameRate) || motion.FrameRate <= 0f)
            AddError(issues, "Frame rate must be a finite value greater than zero.");

        if (motion.Curves == null || motion.Curves.Count == 0)
        {
            AddError(issues, "At least one muscle curve is required.");
            return issues;
        }

        HashSet<string> usedMuscles = new(StringComparer.Ordinal);

        for (int curveIndex = 0; curveIndex < motion.Curves.Count; curveIndex++)
        {
            HumanoidMuscleCurve curve = motion.Curves[curveIndex];
            if (curve == null)
            {
                AddError(issues, $"Curve {curveIndex} is null.");
                continue;
            }

            string muscleName = curve.MuscleName;
            if (!ValidMuscleNames.Contains(muscleName))
                AddError(issues, $"Unknown Humanoid muscle: '{muscleName}'.");

            if (!usedMuscles.Add(muscleName ?? string.Empty))
                AddError(issues, $"Duplicate muscle curve: '{muscleName}'.");

            ValidateKeys(motion, curve, issues);
        }

        return issues;
    }

    public static bool TryGenerate(
        HumanoidMotionDefinition motion,
        out AnimationClip clip)
    {
        clip = null;
        IReadOnlyList<MotionValidationIssue> issues = Validate(motion);

        foreach (MotionValidationIssue issue in issues)
        {
            if (issue.Severity != MotionValidationSeverity.Error) continue;
            Debug.LogError($"Cannot generate '{motion?.DisplayName}': {issue.Message}");
            return false;
        }

        EnsureAssetFolder(motion.OutputFolder);
        clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motion.OutputPath);

        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, motion.OutputPath);
        }
        else
        {
            Undo.RecordObject(clip, $"Regenerate {motion.DisplayName}");
            clip.ClearCurves();
        }

        clip.name = motion.ClipName;
        clip.frameRate = motion.FrameRate;

        foreach (HumanoidMuscleCurve muscleCurve in motion.Curves)
            SetMuscleCurve(clip, muscleCurve);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = motion.Loop;
        settings.stopTime = motion.Duration;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(motion.OutputPath, ImportAssetOptions.ForceUpdate);
        Selection.activeObject = clip;
        EditorGUIUtility.PingObject(clip);
        Debug.Log($"Generated Humanoid animation: {motion.OutputPath}");
        return true;
    }

    private static void ValidateKeys(
        HumanoidMotionDefinition motion,
        HumanoidMuscleCurve curve,
        List<MotionValidationIssue> issues)
    {
        if (curve.Keys == null || curve.Keys.Count == 0)
        {
            AddError(issues, $"'{curve.MuscleName}' has no keys.");
            return;
        }

        float previousTime = -1f;
        for (int keyIndex = 0; keyIndex < curve.Keys.Count; keyIndex++)
        {
            HumanoidMotionKey key = curve.Keys[keyIndex];
            string keyLabel = $"'{curve.MuscleName}' key {keyIndex}";

            if (!IsFinite(key.Time) || key.Time < 0f)
                AddError(issues, $"{keyLabel} has an invalid time.");
            else if (key.Time <= previousTime)
                AddError(issues, $"{keyLabel} time must be greater than the previous key.");
            else if (key.Time > motion.Duration)
                AddError(issues, $"{keyLabel} exceeds duration {motion.Duration:0.###}.");

            if (!IsFinite(key.Value))
                AddError(issues, $"{keyLabel} has a non-finite value.");
            else if (key.Value < -1f || key.Value > 1f)
                AddError(issues, $"{keyLabel} value must be between -1 and 1.");

            previousTime = key.Time;
        }

        if (curve.Keys[0].Time > 0f)
        {
            issues.Add(new MotionValidationIssue(
                MotionValidationSeverity.Warning,
                $"'{curve.MuscleName}' starts after time 0."));
        }
    }

    private static void SetMuscleCurve(
        AnimationClip clip,
        HumanoidMuscleCurve muscleCurve)
    {
        Keyframe[] keys = new Keyframe[muscleCurve.Keys.Count];
        for (int i = 0; i < muscleCurve.Keys.Count; i++)
        {
            HumanoidMotionKey key = muscleCurve.Keys[i];
            keys[i] = new Keyframe(key.Time, key.Value);
        }

        AnimationCurve curve = new(keys);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(
                curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(
                curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }

        EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
            string.Empty,
            typeof(Animator),
            muscleCurve.MuscleName);
        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        string[] parts = assetFolder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void AddError(List<MotionValidationIssue> issues, string message)
    {
        issues.Add(new MotionValidationIssue(MotionValidationSeverity.Error, message));
    }
}
