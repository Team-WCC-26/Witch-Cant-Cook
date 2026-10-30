using UnityEngine;

/// <summary>Layers luminous filaments and halos along the existing ingredient path.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ConveyorMagicFlowVisual : MonoBehaviour
{
    [Header("경로와 렌더러")]
    [SerializeField] private ConveyorPath path;
    [SerializeField] private LineRenderer[] backgroundRibbons = new LineRenderer[1];

    [Header("범위")]
    [Min(0.1f)] [SerializeField] private float spread = 1.5f;
    [SerializeField] private float heightOffset = 0.10f;
    [Min(0.1f)] [SerializeField] private float ribbonWidthScale = 4f;
    [Min(0.1f)] [SerializeField] private float ribbonSize = 0.75f;
    [Range(2, 24)] [SerializeField] private int samplesPerMeter = 10;

    [Header("물결")]
    [Min(0f)] [SerializeField] private float waveAmplitude = 0.17f;
    [Min(0.25f)] [SerializeField] private float waveLength = 5.4f;
    [Min(0f)] [SerializeField] private float waveSpeed = 1.4f;
    [Min(0f)] [SerializeField] private float lateralWaveAmplitude = 0.045f;
    [Tooltip("고정된 난수 시드입니다. 같은 시드에서는 물결이 연속적으로 흐르고 갑자기 바뀌지 않습니다.")]
    [SerializeField] private int waveSeed = 731;

    private static readonly float[] RibbonWidths = { 0.48f, 0.38f };
    private Vector3[] routePoints;
    private Vector3[] routeRight;
    private Vector3[] renderPoints;
    private float[] distances;
    private int layoutHash;
    private bool rebuildRequested = true;

    public ConveyorPath Path => path;

    private void OnEnable() => rebuildRequested = true;
    private void OnValidate() => rebuildRequested = true;

    private void Update()
    {
        if (rebuildRequested || GetLayoutHash() != layoutHash) Rebuild();
        if (Application.isPlaying) RenderAtTime(Time.time);
    }

    [ContextMenu("경로 이펙트 다시 생성")]
    public void Rebuild()
    {
        rebuildRequested = false;
        if (path == null) return;
        path.Build();
        if (path.TotalLength <= 0f)
        {
            if (backgroundRibbons != null)
                foreach (LineRenderer line in backgroundRibbons) if (line != null) line.positionCount = 0;
            routePoints = null;
            layoutHash = GetLayoutHash();
            return;
        }

        int count = Mathf.Clamp(Mathf.CeilToInt(path.TotalLength * samplesPerMeter) + 1, 8, 768);
        routePoints = new Vector3[count];
        routeRight = new Vector3[count];
        renderPoints = new Vector3[count];
        distances = new float[count];
        for (int i = 0; i < count; i++)
        {
            distances[i] = path.TotalLength * i / (count - 1f);
            var sample = path.Evaluate(distances[i]);
            routePoints[i] = sample.point;
            routeRight[i] = Vector3.Cross(Vector3.up, sample.tangent).normalized;
        }
        layoutHash = GetLayoutHash();
        RenderAtTime(Application.isPlaying ? Time.time : 0f);
    }

    // Same curve generation is used in gameplay and the editor's rendered previews.
    public void RenderAtTime(float time)
    {
        if (routePoints == null || backgroundRibbons == null || path == null) return;
        float total = path.TotalLength;
        timeForGeometry = time;
        for (int i = 0; i < backgroundRibbons.Length; i++)
        {
            LineRenderer line = backgroundRibbons[i];
            if (line == null) continue;
            float center = i - (backgroundRibbons.Length - 1) * 0.5f;
            float phase = i * Mathf.PI;
            for (int p = 0; p < routePoints.Length; p++)
            {
                float d = distances[p];
                float envelope = Mathf.SmoothStep(0, 1, Mathf.Clamp01(d / 0.8f))
                    * Mathf.SmoothStep(0, 1, Mathf.Clamp01((total - d) / 0.8f));
                float drift = d - time * waveSpeed * 0.28f;
                float wave = SmoothNoise(drift / (waveLength * 0.56f), waveSeed + i * 47);
                float height = center * 0.17f + wave * waveAmplitude * envelope;
                float side = center * 0.12f
                    + SmoothNoise(drift / (waveLength * 0.71f), waveSeed + 173 + i * 29)
                    * lateralWaveAmplitude * envelope;
                renderPoints[p] = routePoints[p] + Vector3.up * (heightOffset + height * spread * ribbonSize)
                    + routeRight[p] * (side * spread * ribbonSize);
            }
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Tile;
            line.widthMultiplier = RibbonWidths[i % RibbonWidths.Length] * ribbonWidthScale * ribbonSize;
            line.positionCount = renderPoints.Length;
            line.SetPositions(renderPoints);
            UpdateGeometry(i, line);
        }
    }

    // One mesh contains the light cores, soft halos and internal fine strands; there is no outer shell.
    private sealed class RibbonGeometry
    {
        public Mesh mesh;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Vector3[] vertices;
        public Vector4[] tangents;
        public Vector3[] centerline;
    }

    private RibbonGeometry[] geometry;
    private float timeForGeometry;

    private void UpdateGeometry(int index, LineRenderer style)
    {
        const int strands = 28; // Two light cores, their halos, and 24 internal filaments.
        var filter = style.GetComponent<MeshFilter>();
        var renderer = style.GetComponent<MeshRenderer>();
        if (filter == null || renderer == null) return;
        if (geometry == null || geometry.Length != backgroundRibbons.Length)
        {
            ReleaseGeometry();
            geometry = new RibbonGeometry[backgroundRibbons.Length];
        }
        int points = renderPoints.Length;
        int strandStride = points * 2;
        int vertexCount = strands * strandStride;
        var surface = geometry[index];
        if (surface == null || surface.mesh == null || surface.vertices.Length != vertexCount)
        {
            if (surface != null && surface.mesh != null)
            {
                if (Application.isPlaying) Destroy(surface.mesh);
                else DestroyImmediate(surface.mesh);
            }
            surface = new RibbonGeometry
            {
                mesh = new Mesh { name = "MagicFlow_LuminousBundle", hideFlags = HideFlags.HideAndDontSave },
                filter = filter,
                renderer = renderer,
                vertices = new Vector3[vertexCount],
                tangents = new Vector4[vertexCount],
                centerline = new Vector3[points]
            };
            surface.mesh.MarkDynamic();
            var uv = new Vector2[vertexCount];
            var filament = new Vector2[vertexCount];
            var colors = new Color[vertexCount];
            var indices = new int[strands * (points - 1) * 6];
            for (int strand = 0; strand < strands; strand++)
            {
                int group = strand % 2;
                float opacity = strand < 2 ? 0.78f : (strand < 4 ? 0.070f : 0.11f + (strand % 4) * 0.025f);
                float phaseOffset = strand < 4 ? 0f : Mathf.Sin(strand * 2.13f) * 0.10f;
                for (int p = 0; p < points; p++)
                {
                    int v = strand * strandStride + p * 2;
                    uv[v] = new Vector2(distances[p], 0);
                    uv[v + 1] = new Vector2(distances[p], 1);
                    filament[v] = filament[v + 1] = new Vector2(group, phaseOffset);
                    colors[v] = colors[v + 1] = new Color(1,1,1,opacity);
                    if (p == points - 1) continue;
                    int t = (strand * (points - 1) + p) * 6;
                    indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                    indices[t + 3] = v + 1; indices[t + 4] = v + 3; indices[t + 5] = v + 2;
                }
            }
            surface.mesh.vertices = surface.vertices;
            surface.mesh.uv = uv;
            surface.mesh.uv2 = filament;
            surface.mesh.colors = colors;
            surface.mesh.triangles = indices;
            geometry[index] = surface;
        }

        float radius = style.widthMultiplier * 0.5f;
        float widthScale = style.widthMultiplier / 1.44f;
        for (int strand = 0; strand < strands; strand++)
        {
            int group = strand % 2;
            int detail = (strand - 4) / 2;
            int familySeed = waveSeed + group * 113;
            int detailSeed = familySeed + detail * 37 + 271;
            for (int p = 0; p < points; p++)
            {
                float d = distances[p];
                float drift = d - timeForGeometry * waveSpeed * (group == 0 ? 0.24f : 0.19f);
                float scale = waveLength * (group == 0 ? 0.39f : 0.53f);
                // Warped, independently seeded curves avoid repeated sine-wave crests.
                float coordinate = drift / scale;
                float warped = coordinate + SmoothNoise(coordinate * 0.43f, familySeed + 19) * 0.55f;
                float side = radius * (SmoothNoise(warped + 3.7f, familySeed + 53) * 0.65f
                    + SmoothNoise(warped * 0.49f, familySeed + 97) * 0.22f);
                float height = radius * (SmoothNoise(warped, familySeed) * 0.85f
                    + SmoothNoise(warped * 1.63f + 7.1f, familySeed + 11) * 0.20f);
                if (strand >= 4)
                {
                    side += ((detail - 5.5f) * 0.012f
                        + SmoothNoise(drift / 1.8f, detailSeed) * 0.045f) * widthScale;
                    height += ((detail - 5.5f) * 0.009f
                        + SmoothNoise(drift / 2.1f, detailSeed + 67) * 0.065f) * widthScale;
                }
                surface.centerline[p] = renderPoints[p] + routeRight[p] * side + Vector3.up * height;
            }
            float width = strand < 2 ? 0.055f + group * 0.010f
                : (strand < 4 ? 0.30f + group * 0.020f : 0.022f + (strand % 4) * 0.004f);
            float halfWidth = width * widthScale * 0.5f;
            for (int p = 0; p < points; p++)
            {
                int before = Mathf.Max(0, p - 1);
                int after = Mathf.Min(points - 1, p + 1);
                Vector3 tangent = (surface.centerline[after] - surface.centerline[before]).normalized;
                Vector3 tangentLocal = filter.transform.InverseTransformDirection(tangent).normalized;
                Vector3 centerLocal = filter.transform.InverseTransformPoint(surface.centerline[p]);
                int v = strand * strandStride + p * 2;
                surface.vertices[v] = surface.vertices[v + 1] = centerLocal;
                surface.tangents[v] = new Vector4(tangentLocal.x,tangentLocal.y,tangentLocal.z,-halfWidth);
                surface.tangents[v + 1] = new Vector4(tangentLocal.x,tangentLocal.y,tangentLocal.z,halfWidth);
            }
        }
        surface.mesh.vertices = surface.vertices;
        surface.mesh.tangents = surface.tangents;
        surface.mesh.RecalculateBounds();
        var bounds = surface.mesh.bounds;
        bounds.Expand(style.widthMultiplier * 0.60f);
        surface.mesh.bounds = bounds;
        filter.sharedMesh = surface.mesh;
        if (renderer.sharedMaterial != style.sharedMaterial) renderer.sharedMaterial = style.sharedMaterial;
        renderer.enabled = true;
        style.enabled = false;
    }

    // Quintic interpolation keeps the random field smooth in space and time.
    private static float SmoothNoise(float coordinate, int seed)
    {
        int knot = Mathf.FloorToInt(coordinate);
        float t = coordinate - knot;
        t = t * t * t * (t * (t * 6f - 15f) + 10f);
        return Mathf.Lerp(KnotValue(knot, seed), KnotValue(knot + 1, seed), t);
    }

    private static float KnotValue(int knot, int seed)
    {
        unchecked
        {
            uint value = (uint)knot * 374761393u + (uint)seed * 668265263u;
            value = (value ^ (value >> 13)) * 1274126177u;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 8388607.5f - 1f;
        }
    }

    private void OnDisable() => ReleaseGeometry();

    private void ReleaseGeometry()
    {
        if (geometry == null) return;
        foreach (var surface in geometry)
        {
            if (surface == null) continue;
            if (surface.filter != null && surface.filter.sharedMesh == surface.mesh)
                surface.filter.sharedMesh = null;
            if (surface.renderer != null) surface.renderer.enabled = false;
            if (surface.mesh == null) continue;
            if (Application.isPlaying) Destroy(surface.mesh);
            else DestroyImmediate(surface.mesh);
        }
        geometry = null;
    }

    private int GetLayoutHash()
    {
        unchecked
        {
            int hash = path != null ? path.GetInstanceID() : 0;
            hash = hash * 31 + spread.GetHashCode();
            hash = hash * 31 + heightOffset.GetHashCode();
            hash = hash * 31 + ribbonWidthScale.GetHashCode();
            hash = hash * 31 + ribbonSize.GetHashCode();
            hash = hash * 31 + samplesPerMeter;
            hash = hash * 31 + waveAmplitude.GetHashCode();
            hash = hash * 31 + waveLength.GetHashCode();
            hash = hash * 31 + waveSpeed.GetHashCode();
            hash = hash * 31 + waveSeed;
            hash = hash * 31 + lateralWaveAmplitude.GetHashCode();
            if (path != null)
            {
                hash = hash * 31 + path.TotalLength.GetHashCode();
                if (path.Waypoints != null)
                    foreach (Transform point in path.Waypoints)
                        hash = hash * 31 + (point != null ? point.position.GetHashCode() : 0);
            }
            if (backgroundRibbons != null)
                foreach (LineRenderer line in backgroundRibbons)
                    hash = hash * 31 + (line != null ? line.GetInstanceID() : 0);
            return hash;
        }
    }
}

