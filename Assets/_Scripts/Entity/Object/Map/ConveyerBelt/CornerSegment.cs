using UnityEngine;

public class CornerSegment : IConveyorSegment
{
    private readonly Vector3 p0, p1, p2;
    private const int SAMPLES = 20;
    private readonly float[] cumulativeLen;
    private readonly float[] tAtSample;
    public float Length { get; }

    public CornerSegment(Vector3 p0, Vector3 p1, Vector3 p2)
    {
        this.p0 = p0; this.p1 = p1; this.p2 = p2;

        cumulativeLen = new float[SAMPLES + 1];
        tAtSample = new float[SAMPLES + 1];

        Vector3 prev = Evaluate(0f);
        float acc = 0f;
        for (int i = 0; i <= SAMPLES; i++)
        {
            float t = i / (float)SAMPLES;
            Vector3 p = Evaluate(t);
            acc += Vector3.Distance(prev, p);
            cumulativeLen[i] = acc;
            tAtSample[i] = t;
            prev = p;
        }
        Length = acc;
    }

    // B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
    private Vector3 Evaluate(float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }

    // B'(t) = 2(1-t)(P1-P0) + 2t(P2-P1)
    private Vector3 EvaluateTangent(float t)
    {
        Vector3 tan = 2f * (1f - t) * (p1 - p0) + 2f * t * (p2 - p1);
        return tan.sqrMagnitude > 0.0001f ? tan.normalized : (p2 - p0).normalized;
    }

    private float DistanceToT(float distance)
    {
        distance = Mathf.Clamp(distance, 0f, Length);
        for (int i = 1; i <= SAMPLES; i++)
        {
            if (cumulativeLen[i] >= distance)
            {
                float segStart = cumulativeLen[i - 1];
                float segEnd = cumulativeLen[i];
                float local = segEnd > segStart ? (distance - segStart) / (segEnd - segStart) : 0f;
                return Mathf.Lerp(tAtSample[i - 1], tAtSample[i], local);
            }
        }
        return 1f;
    }

    public Vector3 GetPoint(float d) => Evaluate(DistanceToT(d));
    public Vector3 GetTangent(float d) => EvaluateTangent(DistanceToT(d));
}