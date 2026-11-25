using UnityEngine;

[DisallowMultipleComponent]
public sealed class InteractionFocusSparkleVfx : MonoBehaviour
{
    [SerializeField] private ParticleSystem[] particles = System.Array.Empty<ParticleSystem>();
    [SerializeField] private Animation loopAnimation;
    [SerializeField] private Transform particleRoot;
    [SerializeField, Min(0f)] private float boundsPadding = .18f;
    [SerializeField, ColorUsage(true, true)] private Color pickupColorMin = new(1f, .62f, .05f, .95f);
    [SerializeField, ColorUsage(true, true)] private Color pickupColorMax = new(1f, .94f, .35f, 1f);

    [SerializeField, Min(0f), InspectorName("색상 전환 시간")] private float colorTransitionDuration = .2f;
    private static readonly int PickupBlendId = Shader.PropertyToID("_PickupBlend");
    private static readonly int PickupMinId = Shader.PropertyToID("_PickupColorMin");
    private static readonly int PickupMaxId = Shader.PropertyToID("_PickupColorMax");
    private static readonly int SourceAlphaId = Shader.PropertyToID("_SourceAlphaRange");
    private static readonly System.Collections.Generic.List<ParticleSystemVertexStream> ColorStreams = new()
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX
    };
    private float colorProgress;
    private float colorTarget;
    public float ColorBlend => Mathf.SmoothStep(0f, 1f, colorProgress);
    private bool hasPickupState;
    private bool pickupReady;
    public bool IsPickupReady => hasPickupState && pickupReady;
    [SerializeField, Min(.05f), InspectorName("파티클 크기 배율")] private float particleSizeMultiplier = 1f;
    [SerializeField, Range(0f, .02f), InspectorName("파티클 최소 화면 크기")] private float minimumScreenParticleSize = .004f;
    private float previousSizeMultiplier = -1f;
    private float previousScreenSize = -1f;
    private float appliedSizeScale;
    private ParticleSystem.Particle[] liveParticles = new ParticleSystem.Particle[64];
    public float ParticleSizeMultiplier
    {
        get => particleSizeMultiplier;
        set => particleSizeMultiplier = Mathf.Max(.05f, value);
    }
    [SerializeField, Min(0f), InspectorName("나타나는 시간")] private float fadeInDuration = .18f;
    [SerializeField, Min(0f), InspectorName("사라지는 시간")] private float fadeOutDuration = .22f;
    private static readonly int OpacityId = Shader.PropertyToID("_EffectOpacity");
    private ParticleSystemRenderer[] particleRenderers;
    private MaterialPropertyBlock opacityBlock;
    private float fadeProgress;
    private float fadeTarget;
    private bool smoothVisibility;
    public float Visibility => Mathf.SmoothStep(0f, 1f, fadeProgress);
    private ParticleSystem.MinMaxCurve[] baseSizes;
    private Vector3 previousSize = -Vector3.one;

    public void Configure(ParticleSystem[] systems, Animation animation, Transform root)
    {
        particles = systems;
        loopAnimation = animation;
        particleRoot = root;
        baseSizes = null;

        hasPickupState = false;
        colorProgress = colorTarget = 0f;
        previousSize = -Vector3.one;
        previousSizeMultiplier = -1f;
        previousScreenSize = -1f;
        appliedSizeScale = 0f;
        particleRenderers = null;
        smoothVisibility = false;
        fadeProgress = fadeTarget = 0f;
    }

    public void Follow(Bounds bounds)
    {
        transform.position = bounds.center;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.Max(bounds.size + Vector3.one * boundsPadding, Vector3.one * .25f);
        if (baseSizes == null)
        {
            baseSizes = new ParticleSystem.MinMaxCurve[particles.Length];
            for (int i = 0; i < particles.Length; i++)
                if (particles[i] != null) baseSizes[i] = particles[i].main.startSize;
        }
        float multiplier = Mathf.Max(.05f, particleSizeMultiplier);
        if (previousSize == bounds.size && Mathf.Approximately(previousSizeMultiplier, multiplier) &&
            Mathf.Approximately(previousScreenSize, minimumScreenParticleSize)) return;
        previousSize = bounds.size;
        previousSizeMultiplier = multiplier;
        previousScreenSize = minimumScreenParticleSize;
        float scale = Mathf.Clamp(bounds.size.magnitude * .65f, .6f, 1.5f) * multiplier;
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null) continue;
            ParticleSystem.MinMaxCurve size = baseSizes[i];
            if (size.mode == ParticleSystemCurveMode.Curve || size.mode == ParticleSystemCurveMode.TwoCurves)
                size.curveMultiplier *= scale;
            else
            {
                size.constantMin *= scale;
                size.constantMax *= scale;
            }
            var main = particles[i].main;
            main.startSize = size;
            var renderer = particles[i].GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
                renderer.minParticleSize = Mathf.Clamp(minimumScreenParticleSize * multiplier, 0f, .05f);
            if (appliedSizeScale <= 0f || Mathf.Approximately(appliedSizeScale, scale)) continue;
            int capacity = Mathf.Max(64, particles[i].particleCount);
            if (liveParticles.Length < capacity) System.Array.Resize(ref liveParticles, capacity);
            int count = particles[i].GetParticles(liveParticles);
            float ratio = scale / appliedSizeScale;
            for (int j = 0; j < count; j++) liveParticles[j].startSize *= ratio;
            particles[i].SetParticles(liveParticles, count);
        }
        appliedSizeScale = scale;
    }

    [ContextMenu("Play Loop")]
    public void PlayLoop()
    {
        smoothVisibility = false;
        fadeProgress = fadeTarget = 1f;
        ApplyVisibility();
        RestartParticles();
    }

    [ContextMenu("Stop Effect")]
    public void StopEffect()
    {
        smoothVisibility = false;
        fadeProgress = fadeTarget = 0f;
        ApplyVisibility();
        if (loopAnimation != null) loopAnimation.Stop();
        if (particleRoot != null) particleRoot.localScale = Vector3.one;
        foreach (ParticleSystem system in particles)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDisable() => StopEffect();


    public void SetPickupReady(bool ready)
    {
        float target = ready ? 1f : 0f;
        if (!hasPickupState || !gameObject.activeInHierarchy)
        {
            hasPickupState = true;
            pickupReady = ready;
            colorProgress = colorTarget = target;
            ApplyVisibility();
            return;
        }
        if (pickupReady == ready) return;
        pickupReady = ready;
        colorTarget = target;
        if (colorTransitionDuration <= 0f) TickColorTransition(0f);
    }


    public void FadeIn()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        smoothVisibility = true;
        fadeTarget = 1f;
        ApplyVisibility();
        bool playing = false;
        foreach (ParticleSystem system in particles)
            if (system != null && system.isPlaying) { playing = true; break; }
        if (!playing) RestartParticles();
        if (fadeInDuration <= 0f) TickFade(0f);
    }

    public void FadeOut()
    {
        if (!gameObject.activeSelf) return;
        smoothVisibility = true;
        fadeTarget = 0f;
        if (fadeOutDuration <= 0f || fadeProgress <= 0f) TickFade(0f);
    }

    private void Update()
    {
        TickColorTransition(Time.unscaledDeltaTime);
        TickFade(Time.unscaledDeltaTime);
    }

    public void TickFade(float deltaTime)
    {
        if (!smoothVisibility) return;
        if (!Mathf.Approximately(fadeProgress, fadeTarget))
        {
            float duration = fadeTarget > fadeProgress ? fadeInDuration : fadeOutDuration;
            fadeProgress = duration <= 0f ? fadeTarget
                : Mathf.MoveTowards(fadeProgress, fadeTarget, Mathf.Max(0f, deltaTime) / duration);
            ApplyVisibility();
        }
        if (fadeTarget == 0f && fadeProgress <= 0f)
        {
            StopEffect();
            gameObject.SetActive(false);
        }
    }

    private void ApplyVisibility()
    {
        if (particleRenderers == null || particleRenderers.Length != particles.Length)
        {
            particleRenderers = new ParticleSystemRenderer[particles.Length];
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] == null) continue;
                particleRenderers[i] = particles[i].GetComponent<ParticleSystemRenderer>();
                if (particleRenderers[i] != null) particleRenderers[i].SetActiveVertexStreams(ColorStreams);
            }
        }
        if (opacityBlock == null) opacityBlock = new MaterialPropertyBlock();
        for (int i = 0; i < particleRenderers.Length; i++)
        {
            ParticleSystemRenderer renderer = particleRenderers[i];
            if (renderer == null) continue;
            renderer.GetPropertyBlock(opacityBlock);
            opacityBlock.SetFloat(OpacityId, Visibility);
            opacityBlock.SetFloat(PickupBlendId, ColorBlend);
            opacityBlock.SetColor(PickupMinId, pickupColorMin);
            opacityBlock.SetColor(PickupMaxId, pickupColorMax);
            var source = particles[i].main.startColor;
            opacityBlock.SetVector(SourceAlphaId, new Vector4(source.colorMin.a, source.colorMax.a, 0f, 0f));
            renderer.SetPropertyBlock(opacityBlock);
        }
    }

    private void RestartParticles()
    {
        if (loopAnimation != null) loopAnimation.Stop();
        if (particleRoot != null) particleRoot.localScale = Vector3.one;
        foreach (ParticleSystem system in particles)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (loopAnimation != null) loopAnimation.Play();
        foreach (ParticleSystem system in particles)
            if (system != null) system.Play(true);
    }


    public void TickColorTransition(float deltaTime)
    {
        if (!hasPickupState || Mathf.Approximately(colorProgress, colorTarget)) return;
        colorProgress = colorTransitionDuration <= 0f ? colorTarget
            : Mathf.MoveTowards(colorProgress, colorTarget, Mathf.Max(0f, deltaTime) / colorTransitionDuration);
        ApplyVisibility();
    }
}
