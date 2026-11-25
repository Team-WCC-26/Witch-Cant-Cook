using UnityEngine;

/// <summary>Uses a hidden emission strip to scatter sparse particles along the conveyor route.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ConveyorMagicSparkles : MonoBehaviour
{
    [SerializeField] private ConveyorPath path;
    [SerializeField] private ParticleSystem[] layers = new ParticleSystem[3];
    [Min(0.01f)] [SerializeField] private float halfWidth = 0.38f;
    [SerializeField] private float height = 0.16f;
    [Min(0f)] [SerializeField] private float heightSpread = 0.22f;
    [Min(0f)] [SerializeField] private float moveSpeed = 1.25f;
    [Min(0.01f)] [SerializeField] private float exitFadeDistance = 0.45f;

    private Mesh emissionMesh;
    private bool rebuildRequested = true;
    private int layoutHash;
    private ParticleSystem.Particle[] particleBuffer;

    private void OnEnable()
    {
        rebuildRequested = true;
        if (path != null) Rebuild();
    }
    private void OnValidate() => rebuildRequested = true;

    private void Update()
    {
        if (rebuildRequested || emissionMesh == null || GetLayoutHash() != layoutHash)
            Rebuild();
    }

    private void LateUpdate() => UpdateParticlePositions();

    // Particle age provides deterministic progress, also used by editor previews.
    public void UpdateParticlePositions()
    {
        if (path == null || path.TotalLength <= 0f || layers == null) return;
        float length = path.TotalLength;
        foreach (ParticleSystem layer in layers)
        {
            if (layer == null) continue;
            int capacity = layer.main.maxParticles;
            if (particleBuffer == null || particleBuffer.Length < capacity)
                particleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(capacity)];
            int count = layer.GetParticles(particleBuffer);
            var baseColor = layer.main.startColor;
            for (int i = 0; i < count; i++)
            {
                var particle = particleBuffer[i];
                uint seed = particle.randomSeed;
                float age = Mathf.Max(0f, particle.startLifetime - particle.remainingLifetime);
                float speed = moveSpeed * Mathf.Lerp(0.85f, 1.15f, Seed01(seed ^ 0x71F3u));
                float distance = Seed01(seed ^ 0x93B1u) * length + age * speed;
                if (distance >= length)
                {
                    particle.remainingLifetime = 0f;
                    particleBuffer[i] = particle;
                    continue;
                }
                var sample = path.Evaluate(distance);
                Vector3 right = Vector3.Cross(Vector3.up, sample.tangent).normalized;
                float side = Mathf.Lerp(-halfWidth, halfWidth, Seed01(seed ^ 0x12E7u));
                float elevation = height + heightSpread * Seed01(seed ^ 0xA351u)
                    + Mathf.Sin(age * 0.9f + Seed01(seed ^ 0xC62Fu) * Mathf.PI * 2f) * 0.025f;
                particle.position = sample.point + right * side + Vector3.up * elevation;
                particle.velocity = Vector3.zero;
                Color color = particle.startColor;
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((length - distance) / exitFadeDistance));
                color.a = baseColor.Evaluate(0f, Seed01(seed ^ 0x64D9u)).a * fade;
                particle.startColor = color;
                particleBuffer[i] = particle;
            }
            layer.SetParticles(particleBuffer, count);
        }
    }

    private static float Seed01(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777216f;
        }
    }

    [ContextMenu("반짝이 방출 경로 갱신")]
    public void Rebuild()
    {
        rebuildRequested = false;
        if (path == null) return;
        path.Build();
        if (path.TotalLength <= 0f) return;
        if (emissionMesh == null)
        {
            emissionMesh = new Mesh { name = "MagicFlow_SparkleEmission", hideFlags = HideFlags.HideAndDontSave };
            emissionMesh.MarkDynamic();
        }
        int samples = Mathf.Clamp(Mathf.CeilToInt(path.TotalLength * 5f) + 1, 8, 384);
        var vertices = new Vector3[samples * 2];
        var triangles = new int[(samples - 1) * 6];
        for (int i = 0; i < samples; i++)
        {
            float distance = path.TotalLength * i / (samples - 1f);
            var sample = path.Evaluate(distance);
            Vector3 right = Vector3.Cross(Vector3.up, sample.tangent).normalized;
            float wave = Mathf.Sin(distance * 0.9f) * heightSpread * 0.25f;
            vertices[i * 2] = transform.InverseTransformPoint(sample.point - right * halfWidth
                + Vector3.up * (height + wave));
            vertices[i * 2 + 1] = transform.InverseTransformPoint(sample.point + right * halfWidth
                + Vector3.up * (height + heightSpread - wave));
            if (i == samples - 1) continue;
            int j = i * 6;
            int v = i * 2;
            triangles[j] = v;
            triangles[j + 1] = v + 2;
            triangles[j + 2] = v + 1;
            triangles[j + 3] = v + 1;
            triangles[j + 4] = v + 2;
            triangles[j + 5] = v + 3;
        }
        emissionMesh.Clear();
        emissionMesh.vertices = vertices;
        emissionMesh.triangles = triangles;
        emissionMesh.RecalculateNormals();
        emissionMesh.RecalculateBounds();
        if (layers != null)
            foreach (ParticleSystem layer in layers)
            {
                if (layer == null) continue;
                var shape = layer.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Mesh;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                shape.mesh = emissionMesh;
            }
        layoutHash = GetLayoutHash();
    }

    private void OnDisable()
    {
        if (emissionMesh == null) return;
        if (layers != null)
            foreach (ParticleSystem layer in layers)
            {
                if (layer == null) continue;
                var shape = layer.shape;
                if (shape.mesh == emissionMesh) shape.mesh = null;
            }
        if (Application.isPlaying) Destroy(emissionMesh);
        else DestroyImmediate(emissionMesh);
        emissionMesh = null;
    }

    private int GetLayoutHash()
    {
        unchecked
        {
            int hash = path != null ? path.GetInstanceID() : 0;
            hash = hash * 31 + halfWidth.GetHashCode();
            hash = hash * 31 + height.GetHashCode();
            hash = hash * 31 + heightSpread.GetHashCode();
            hash = hash * 31 + transform.localToWorldMatrix.GetHashCode();
            if (path != null && path.Waypoints != null)
                foreach (Transform point in path.Waypoints)
                    hash = hash * 31 + (point != null ? point.position.GetHashCode() : 0);
            return hash;
        }
    }
}
