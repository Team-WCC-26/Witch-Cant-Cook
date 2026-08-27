using UnityEngine;

public class StraightSegment : IConveyorSegment
{
    private readonly Vector3 start;
    private readonly Vector3 direction;
    public float Length { get; }

    public StraightSegment(Vector3 start, Vector3 end)
    {
        this.start = start;
        Length = Vector3.Distance(start, end);
        direction = Length > 0.0001f ? (end - start).normalized : Vector3.forward;
    }

    public Vector3 GetPoint(float d) => start + direction * Mathf.Clamp(d, 0f, Length);
    public Vector3 GetTangent(float d) => direction;
}