using System.Collections.Generic;
using UnityEditor;

public sealed class SlashMotionDefinition : HumanoidMotionDefinition
{
    // Times and values below are the preserved production Slash motion.
    private const float BaseTime = 0f;
    private const float PreparationTime = 0.30f;
    private const float PreparationHoldTime = 0.38f;
    private const float ResultTime = 0.52f;
    private const float ResultHoldTime = 0.58f;
    private const float RecoveryTime = 0.92f;

    public override string DisplayName => "Slash";
    public override string ClipName => "Slash";
    public override string OutputFolder => "Assets/Animations/Player/Upper/Generated";
    public override float Duration => RecoveryTime;

    private static readonly HumanoidMuscleCurve[] MotionCurves =
    {
        SlashMuscle("Right Arm Down-Up", -0.6432f, 0f, -0.15185f),
        SlashMuscle("Right Arm Front-Back", 0.13627f, -0.4f, 0.02630f),
        SlashMuscle("Right Arm Twist In-Out", -0.13433f, -0.13433f, -0.0664f),
        SlashMuscle("Right Forearm Stretch", 0.96324f, 0f, 0.96324f),
        SlashMuscle("Right Hand In-Out", 0.01080f, 0.01080f, 0.4772f),

        // The unused arm stays in the measured relaxed pose on the upper-body layer.
        SlashMuscle("Left Shoulder Down-Up", 0.01671f, 0.01671f, 0.01671f),
        SlashMuscle("Left Shoulder Front-Back", 0.23102f, 0.23102f, 0.23102f),
        SlashMuscle("Left Arm Down-Up", -0.4143f, -0.4143f, -0.4143f),
        SlashMuscle("Left Arm Front-Back", -0.0012f, -0.0012f, -0.0012f),
        SlashMuscle("Left Arm Twist In-Out", -0.7465f, -0.7465f, -0.7465f),
        SlashMuscle("Left Forearm Stretch", 0.87162f, 0.87162f, 0.87162f),
        SlashMuscle("Left Forearm Twist In-Out", 0.76548f, 0.76548f, 0.76548f),
        SlashMuscle("Left Hand Down-Up", -0.1509f, -0.1509f, -0.1509f),
        SlashMuscle("Left Hand In-Out", 0.13197f, 0.13197f, 0.13197f),
    };

    public override IReadOnlyList<HumanoidMuscleCurve> Curves => MotionCurves;

    private static HumanoidMuscleCurve SlashMuscle(
        string muscleName,
        float baseValue,
        float preparationValue,
        float resultValue)
    {
        return Muscle(
            muscleName,
            Key(BaseTime, baseValue),
            Key(PreparationTime, preparationValue),
            Key(PreparationHoldTime, preparationValue),
            Key(ResultTime, resultValue),
            Key(ResultHoldTime, resultValue),
            Key(RecoveryTime, baseValue));
    }
}

// Retains the original menu command while using the shared generation pipeline.
public static class SlashAnimationGenerator
{
    [MenuItem("Tools/Player Animation/Generate Slash Animation")]
    private static void Generate()
    {
        HumanoidAnimationClipBuilder.TryGenerate(new SlashMotionDefinition(), out _);
    }
}
