using System.Collections.Generic;

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
        // Stage 1: let the torso clearly lead the arm through the wind-up and follow-through.
        SlashMuscle("Spine Twist Left-Right", 0f, -0.14f, 0.18f),
        SlashMuscle("Chest Twist Left-Right", 0f, -0.20f, 0.30f),

        // Stage 2: shift the upper body back during preparation and into the strike.
        SlashMuscle("Spine Front-Back", 0f, 0.06f, -0.18f),
        SlashMuscle("Chest Front-Back", 0f, 0.08f, -0.26f),

        // Stage 3: carry the sword arm from the torso through the right shoulder.
        SlashMuscle("Right Shoulder Down-Up", 0f, 0.10f, -0.05f),
        SlashMuscle("Right Shoulder Front-Back", 0f, -0.12f, 0.08f),

        SlashMuscle("Right Arm Down-Up", -0.6432f, 0f, -0.15185f),
        SlashMuscle("Right Arm Front-Back", 0.13627f, -0.4f, 0.02630f),
        SlashMuscle("Right Arm Twist In-Out", -0.13433f, -0.13433f, -0.0664f),
        SlashMuscle("Right Forearm Stretch", 0.96324f, 0f, 0.96324f),
        SlashMuscle("Right Hand In-Out", 0.01080f, 0.01080f, 0.4772f),

        // Stage 4: give the free arm a small opposing motion for balance.
        SlashMuscle("Left Shoulder Down-Up", 0.01671f, 0.01671f, 0.01671f),
        SlashMuscle("Left Shoulder Front-Back", 0.23102f, 0.16f, 0.30f),
        SlashMuscle("Left Arm Down-Up", -0.4143f, -0.50f, -0.34f),
        SlashMuscle("Left Arm Front-Back", -0.0012f, -0.12f, 0.10f),
        SlashMuscle("Left Arm Twist In-Out", -0.7465f, -0.7465f, -0.7465f),
        SlashMuscle("Left Forearm Stretch", 0.87162f, 0.58f, 0.70f),
        SlashMuscle("Left Forearm Twist In-Out", 0.76548f, 0.62f, 0.70f),
        SlashMuscle("Left Hand Down-Up", -0.1509f, -0.24f, -0.08f),
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
