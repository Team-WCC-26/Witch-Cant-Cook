using UnityEngine;

[DisallowMultipleComponent]
public sealed class InteractionFocusSparkleVfx : MonoBehaviour
{
    [SerializeField] private ParticleSystem[] particles = System.Array.Empty<ParticleSystem>();
    [SerializeField] private Animation loopAnimation;
    [SerializeField] private Transform particleRoot;
    [SerializeField, Min(0f)] private float boundsPadding = .18f;
    private ParticleSystem.MinMaxCurve[] baseSizes;
    private Vector3 previousSize = -Vector3.one;

    public void Configure(ParticleSystem[] systems, Animation animation, Transform root)
    {
        particles = systems;
        loopAnimation = animation;
        particleRoot = root;
        baseSizes = null;
        previousSize = -Vector3.one;
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
        if (previousSize == bounds.size) return;
        previousSize = bounds.size;
        float scale = Mathf.Clamp(bounds.size.magnitude * .65f, .25f, 1.5f);
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null) continue;
            ParticleSystem.MinMaxCurve size = baseSizes[i];
            size.constantMin *= scale;
            size.constantMax *= scale;
            var main = particles[i].main;
            main.startSize = size;
        }
    }

    [ContextMenu("Play Loop")]
    public void PlayLoop()
    {
        StopEffect();
        if (loopAnimation != null) loopAnimation.Play();
        foreach (ParticleSystem system in particles)
            if (system != null) system.Play(true);
    }

    [ContextMenu("Stop Effect")]
    public void StopEffect()
    {
        if (loopAnimation != null) loopAnimation.Stop();
        if (particleRoot != null) particleRoot.localScale = Vector3.one;
        foreach (ParticleSystem system in particles)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDisable() => StopEffect();
}
