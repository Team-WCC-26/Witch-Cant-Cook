using UnityEngine;

/// <summary>Overlapping soft volume splats with independently varying cross sections.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ConveyorMagicGradientBackground : MonoBehaviour
{
    [Header("경로")]
    [SerializeField] private ConveyorPath path;
    [SerializeField] private float heightOffset = 0.50f;
    [Range(2, 8)] [SerializeField] private int slicesPerMeter = 4;

    [Header("불규칙한 단면 크기")]
    [Tooltip("단면 가로/세로 지름의 최솟값입니다. 유니티 월드 단위입니다.")]
    [Min(0.05f)] [SerializeField] private float minDiameter = 0.6f;
    [Tooltip("단면 가로/세로 지름의 최댓값입니다. 유니티 월드 단위입니다.")]
    [Min(0.05f)] [SerializeField] private float maxDiameter = 1.8f;
    [Min(0.25f)] [SerializeField] private float variationLength = 1.8f;
    [Min(0f)] [SerializeField] private float variationSpeed = 0.45f;
    [SerializeField] private int randomSeed = 731;

    private int layoutHash;
    private bool rebuildRequested = true;
    private Mesh generatedMesh;
    private MaterialPropertyBlock properties;
    public ConveyorPath Path => path;

    private void OnEnable() => rebuildRequested = true;
    private void OnValidate() => rebuildRequested = true;
    private void Update()
    {
        if (rebuildRequested || GetLayoutHash() != layoutHash) Rebuild();
    }

    [ContextMenu("배경 그라데이션 경로 다시 생성")]
    public void Rebuild()
    {
        rebuildRequested = false;
        // A standalone prefab retains its baked mesh and material defaults.
        if (path == null) return;
        var filter = GetComponent<MeshFilter>();
        var renderer = GetComponent<MeshRenderer>();
        if (filter == null || renderer == null) return;
        path.Build();
        if (path.TotalLength <= 0f)
        {
            ReleaseMesh();
            layoutHash = GetLayoutHash();
            return;
        }

        int count = Mathf.Clamp(Mathf.CeilToInt(path.TotalLength * slicesPerMeter) + 1, 8, 256);
        float spacing = path.TotalLength / (count - 1f);
        var vertices = new Vector3[count * 4];
        var tangents = new Vector4[count * 4];
        var uv = new Vector2[count * 4];
        var samples = new Vector2[count * 4];
        var triangles = new int[count * 6];
        for (int i = 0; i < count; i++)
        {
            float distance = spacing * i;
            var sample = path.Evaluate(distance);
            var center = transform.InverseTransformPoint(sample.point + Vector3.up * heightOffset);
            var tangent = transform.InverseTransformDirection(sample.tangent).normalized;
            int v = i * 4, t = i * 6;
            for (int c = 0; c < 4; c++)
            {
                vertices[v + c] = center;
                tangents[v + c] = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
                samples[v + c] = new Vector2(distance, spacing);
            }
            uv[v] = new Vector2(-1f, -1f);
            uv[v + 1] = new Vector2(1f, -1f);
            uv[v + 2] = new Vector2(-1f, 1f);
            uv[v + 3] = new Vector2(1f, 1f);
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
        }
        if (generatedMesh == null)
            generatedMesh = new Mesh { name = "MagicConveyor_SoftGradientVolume", hideFlags = HideFlags.HideAndDontSave };
        generatedMesh.Clear();
        generatedMesh.vertices = vertices;
        generatedMesh.tangents = tangents;
        generatedMesh.uv = uv;
        generatedMesh.uv2 = samples;
        generatedMesh.triangles = triangles;
        generatedMesh.RecalculateBounds();
        var bounds = generatedMesh.bounds;
        float diameter = Mathf.Max(minDiameter, maxDiameter);
        Vector3 scale = transform.lossyScale;
        bounds.Expand(new Vector3(diameter / Mathf.Max(Mathf.Abs(scale.x), 0.001f),
            diameter / Mathf.Max(Mathf.Abs(scale.y), 0.001f),
            diameter / Mathf.Max(Mathf.Abs(scale.z), 0.001f)));
        generatedMesh.bounds = bounds;
        filter.sharedMesh = generatedMesh;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (properties == null) properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetFloat("_PathLength", path.TotalLength);
        properties.SetFloat("_MinDiameter", Mathf.Min(minDiameter, maxDiameter));
        properties.SetFloat("_MaxDiameter", Mathf.Max(minDiameter, maxDiameter));
        properties.SetFloat("_VariationLength", variationLength);
        properties.SetFloat("_VariationSpeed", variationSpeed);
        properties.SetFloat("_RandomSeed", randomSeed);
        renderer.SetPropertyBlock(properties);
        layoutHash = GetLayoutHash();
    }

    private void OnDisable() => ReleaseMesh();
    private void ReleaseMesh()
    {
        if (generatedMesh == null) return;
        var filter = GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh == generatedMesh) filter.sharedMesh = null;
        if (Application.isPlaying) Destroy(generatedMesh);
        else DestroyImmediate(generatedMesh);
        generatedMesh = null;
    }

    private int GetLayoutHash()
    {
        unchecked
        {
            int hash = minDiameter.GetHashCode();
            hash = hash * 31 + maxDiameter.GetHashCode();
            hash = hash * 31 + variationLength.GetHashCode();
            hash = hash * 31 + variationSpeed.GetHashCode();
            hash = hash * 31 + randomSeed;
            hash = hash * 31 + heightOffset.GetHashCode();
            hash = hash * 31 + slicesPerMeter;
            hash = hash * 31 + transform.localToWorldMatrix.GetHashCode();
            hash = hash * 31 + (path != null ? path.GetInstanceID() : 0);
            if (path != null)
            {
                hash = hash * 31 + path.TotalLength.GetHashCode();
                if (path.Waypoints != null)
                    foreach (var point in path.Waypoints)
                        hash = hash * 31 + (point != null ? point.position.GetHashCode() : 0);
            }
            return hash;
        }
    }
}
