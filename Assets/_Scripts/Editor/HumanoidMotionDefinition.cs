using System.Collections.Generic;

// Pure motion data. Unity asset creation is handled by HumanoidAnimationClipBuilder.
public readonly struct HumanoidMotionKey
{
    public readonly float Time;
    public readonly float Value;

    public HumanoidMotionKey(float time, float value)
    {
        Time = time;
        Value = value;
    }
}

public sealed class HumanoidMuscleCurve
{
    public string MuscleName { get; }
    public IReadOnlyList<HumanoidMotionKey> Keys { get; }

    public HumanoidMuscleCurve(string muscleName, params HumanoidMotionKey[] keys)
    {
        MuscleName = muscleName;
        Keys = keys;
    }
}

public abstract class HumanoidMotionDefinition
{
    public abstract string DisplayName { get; }
    public abstract string ClipName { get; }
    public abstract string OutputFolder { get; }
    public abstract float Duration { get; }
    public virtual float FrameRate => 30f;
    public virtual bool Loop => false;
    public abstract IReadOnlyList<HumanoidMuscleCurve> Curves { get; }

    public string OutputPath => $"{OutputFolder}/{ClipName}.anim";

    protected static HumanoidMotionKey Key(float time, float value)
    {
        return new HumanoidMotionKey(time, value);
    }

    protected static HumanoidMuscleCurve Muscle(
        string muscleName,
        params HumanoidMotionKey[] keys)
    {
        return new HumanoidMuscleCurve(muscleName, keys);
    }
}