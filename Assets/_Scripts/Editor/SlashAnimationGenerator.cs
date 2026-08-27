using UnityEditor;
using UnityEngine;

public static class SlashAnimationGenerator
{
    private const string OutputFolder = "Assets/Animations/Player/Upper/Generated";
    private const string OutputClipPath = OutputFolder + "/Slash.anim";

    private const float BaseTime = 0f;
    private const float PreparationTime = 0.28f;
    private const float PreparationHoldTime = 0.34f;
    private const float ResultTime = 0.55f;
    private const float ResultHoldTime = 0.62f;
    private const float RecoveryTime = 0.95f;

    private readonly struct MuscleTrack
    {
        public readonly string Name;
        public readonly float Base;
        public readonly float Preparation;
        public readonly float Result;

        public MuscleTrack(string name, float baseValue, float preparation, float result)
        {
            Name = name;
            Base = baseValue;
            Preparation = preparation;
            Result = result;
        }
    }

    // Only the muscles explicitly measured from the three reference poses are animated.
    private static readonly MuscleTrack[] Tracks =
    {
        new("Right Arm Down-Up", -0.6432f, 0f, -0.15185f),
        new("Right Arm Front-Back", 0.13627f, -0.4f, 0.02630f),
        new("Right Arm Twist In-Out", -0.13433f, -0.13433f, -0.0664f),
        new("Right Forearm Stretch", 0.96324f, 0f, 0.96324f),
        new("Right Hand In-Out", 0.01080f, 0.01080f, 0.4772f),

        // Keep the unused arm in the measured relaxed idle pose throughout the slash.
        new("Left Shoulder Down-Up", 0.01671f, 0.01671f, 0.01671f),
        new("Left Shoulder Front-Back", 0.23102f, 0.23102f, 0.23102f),
        new("Left Arm Down-Up", -0.4143f, -0.4143f, -0.4143f),
        new("Left Arm Front-Back", -0.0012f, -0.0012f, -0.0012f),
        new("Left Arm Twist In-Out", -0.7465f, -0.7465f, -0.7465f),
        new("Left Forearm Stretch", 0.87162f, 0.87162f, 0.87162f),
        new("Left Forearm Twist In-Out", 0.76548f, 0.76548f, 0.76548f),
        new("Left Hand Down-Up", -0.1509f, -0.1509f, -0.1509f),
        new("Left Hand In-Out", 0.13197f, 0.13197f, 0.13197f),
    };

    [MenuItem("Tools/Player Animation/Generate Slash Animation")]
    private static void Generate()
    {
        AnimationClip clip = LoadOrCreateOutputClip();

        foreach (MuscleTrack track in Tracks)
        {
            AnimationCurve curve = new(
                new Keyframe(BaseTime, track.Base),
                new Keyframe(PreparationTime, track.Preparation),
                new Keyframe(PreparationHoldTime, track.Preparation),
                new Keyframe(ResultTime, track.Result),
                new Keyframe(ResultHoldTime, track.Result),
                new Keyframe(RecoveryTime, track.Base));

            SetMuscleCurve(clip, track.Name, curve);
        }

        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = clip;
        Debug.Log("Generated Slash.anim from the measured muscle values.");
    }

    private static AnimationClip LoadOrCreateOutputClip()
    {
        EnsureOutputFolder();
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClipPath);

        if (clip == null)
        {
            clip = new AnimationClip { frameRate = 30f };
            AssetDatabase.CreateAsset(clip, OutputClipPath);
        }
        else
        {
            clip.ClearCurves();
            clip.frameRate = 30f;
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.stopTime = RecoveryTime;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void SetMuscleCurve(AnimationClip clip, string muscleName, AnimationCurve curve)
    {
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }

        EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
            string.Empty,
            typeof(Animator),
            muscleName);
        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }

    private static void EnsureOutputFolder()
    {
        const string parentFolder = "Assets/Animations/Player/Upper";
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder(parentFolder, "Generated");
    }
}
