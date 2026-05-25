using UnityEngine;

public class JointScaleDebug : MonoBehaviour
{
    [SerializeField] private Transform hips;
    [SerializeField] private Transform leftUpperLeg;
    [SerializeField] private Transform rightUpperLeg;

    [ContextMenu("Debug Scale")]
    private void DebugScale()
    {
        PrintScale(hips);
        PrintScale(leftUpperLeg);
        PrintScale(rightUpperLeg);
    }

    private void PrintScale(Transform t)
    {
        if (t == null)
        {
            Debug.LogError("Transform이 비어 있음");
            return;
        }

        Debug.Log(
            $"[{t.name}] " +
            $"localScale = {t.localScale}, " +
            $"lossyScale = {t.lossyScale}"
        );
    }
}