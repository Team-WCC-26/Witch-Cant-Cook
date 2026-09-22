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

        bool copiesSourceClip = !string.IsNullOrWhiteSpace(motion.SourceClipPath);
        if (copiesSourceClip)
        {
            AnimationClip source = AssetDatabase.LoadAssetAtPath<AnimationClip>(motion.SourceClipPath);
            if (source == null)
                AddError(issues, $"Source AnimationClip was not found: '{motion.SourceClipPath}'.");
            bool removesRange = motion.CutEndTime > motion.CutStartTime;
            if (motion.CutStartTime < 0f || motion.CutEndTime < 0f)
                AddError(issues, "Source clip cut range cannot be negative.");
            if (source != null && removesRange && motion.CutEndTime > source.length)
                AddError(issues, "Source clip cut range exceeds its duration.");
            if (motion.CutSeamDuration < 0f)
                AddError(issues, "Cut seam duration cannot be negative.");
            if (!IsFinite(motion.SourceTimeScale) || motion.SourceTimeScale <= 0f)
                AddError(issues, "Source time scale must be greater than zero.");
            return issues;
        }

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
        AnimationClip sourceClip = string.IsNullOrWhiteSpace(motion.SourceClipPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<AnimationClip>(motion.SourceClipPath);
        EditorCurveBinding[] sourceBindings = sourceClip == null
            ? Array.Empty<EditorCurveBinding>()
            : AnimationUtility.GetCurveBindings(sourceClip);
        AnimationCurve[] sourceCurves = new AnimationCurve[sourceBindings.Length];
        for (int i = 0; i < sourceBindings.Length; i++)
            sourceCurves[i] = AnimationUtility.GetEditorCurve(sourceClip, sourceBindings[i]);
        float sourceFrameRate = sourceClip == null ? motion.FrameRate : sourceClip.frameRate;

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

        if (!string.IsNullOrWhiteSpace(motion.SourceClipPath))
        {
            clip.frameRate = sourceFrameRate;
            CopySourceCurvesWithCut(clip, sourceBindings, sourceCurves, motion);
            foreach (HumanoidMuscleCurve muscleCurve in motion.Curves)
                SetMuscleCurve(clip, muscleCurve);
        }
        else
        {
            clip.frameRate = motion.FrameRate;
            foreach (HumanoidMuscleCurve muscleCurve in motion.Curves)
                SetMuscleCurve(clip, muscleCurve);
        }

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

    private static void CopySourceCurvesWithCut(
        AnimationClip destination,
        IReadOnlyList<EditorCurveBinding> sourceBindings,
        IReadOnlyList<AnimationCurve> sourceCurves,
        HumanoidMotionDefinition motion)
    {
        float cutStart = motion.CutStartTime;
        float cutEnd = motion.CutEndTime;
        float seamEnd = cutStart + motion.CutSeamDuration;
        float removedTime = cutEnd - seamEnd;
        bool removesRange = cutEnd > cutStart;

        for (int bindingIndex = 0; bindingIndex < sourceBindings.Count; bindingIndex++)
        {
            EditorCurveBinding binding = sourceBindings[bindingIndex];
            AnimationCurve sourceCurve = sourceCurves[bindingIndex];

            if (!removesRange)
            {
                Keyframe[] scaledKeys = sourceCurve.keys;
                for (int keyIndex = 0; keyIndex < scaledKeys.Length; keyIndex++)
                {
                    scaledKeys[keyIndex].time *= motion.SourceTimeScale;
                    scaledKeys[keyIndex].inTangent /= motion.SourceTimeScale;
                    scaledKeys[keyIndex].outTangent /= motion.SourceTimeScale;
                }
                AnimationUtility.SetEditorCurve(
                    destination, binding, new AnimationCurve(scaledKeys));
                continue;
            }

            List<Keyframe> keys = new();

            foreach (Keyframe key in sourceCurve.keys)
            {
                if (key.time < cutStart)
                    keys.Add(key);
            }

            keys.Add(new Keyframe(cutStart, sourceCurve.Evaluate(cutStart)));
            keys.Add(new Keyframe(seamEnd, sourceCurve.Evaluate(cutEnd)));

            foreach (Keyframe sourceKey in sourceCurve.keys)
            {
                if (sourceKey.time <= cutEnd) continue;

                Keyframe shiftedKey = sourceKey;
                shiftedKey.time -= removedTime;
                keys.Add(shiftedKey);
            }

            AnimationCurve destinationCurve = new(keys.ToArray());
            for (int i = 0; i < destinationCurve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    destinationCurve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(
                    destinationCurve, i, AnimationUtility.TangentMode.ClampedAuto);
            }

            AnimationUtility.SetEditorCurve(destination, binding, destinationCurve);
        }
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
