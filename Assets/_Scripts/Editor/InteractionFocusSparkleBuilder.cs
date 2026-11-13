#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class InteractionFocusSparkleBuilder
{
    public const string Folder = "Assets/_Scripts/Resources/VFX/InteractionFocusSparkle";
    public const string PrefabPath = Folder + "/InteractionFocusSparkle.prefab";

    [MenuItem("Tools/VFX/Rebuild Interaction Focus Sparkle")]
    public static void CreateAssets()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/FocusSparkle.shader");
        if (shader == null) throw new System.InvalidOperationException("FocusSparkle.shader is missing.");
        Material diamonds = MaterialAsset("BlueStar", shader, 1f, 1.15f);
        Material dots = MaterialAsset("BlueGlow", shader, 0f, 1.1f);
        AnimationClip clip = AnimationAsset();
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        InteractionFocusSparkleVfx existing = existingPrefab != null
            ? existingPrefab.GetComponent<InteractionFocusSparkleVfx>() : null;
        var root = new GameObject("Interaction Focus Particles");
        try
        {
            var pivot = new GameObject("Particles").transform;
            pivot.SetParent(root.transform, false);
            ParticleSystem diamondSystem = Particle(pivot, "Floating Diamonds", diamonds, true);
            ParticleSystem dotSystem = Particle(pivot, "Floating Dots", dots, false);
            var animation = root.AddComponent<Animation>();
            animation.AddClip(clip, clip.name);
            animation.clip = clip;
            animation.playAutomatically = false;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            animation.wrapMode = WrapMode.Loop;
            var effect = root.AddComponent<InteractionFocusSparkleVfx>();
            if (existing != null)
            {
                var source = new SerializedObject(existing);
                var destination = new SerializedObject(effect);
                foreach (string property in new[] { "boundsPadding", "particleSizeMultiplier", "minimumScreenParticleSize", "fadeInDuration", "fadeOutDuration", "colorTransitionDuration" })
                    destination.FindProperty(property).floatValue = source.FindProperty(property).floatValue;
                foreach (string property in new[] { "pickupColorMin", "pickupColorMax" })
                    destination.FindProperty(property).colorValue = source.FindProperty(property).colorValue;
                destination.ApplyModifiedPropertiesWithoutUndo();
            }
            effect.Configure(new[] { diamondSystem, dotSystem }, animation, pivot);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssetIfDirty(diamonds);
            AssetDatabase.SaveAssetIfDirty(dots);
            AssetDatabase.SaveAssetIfDirty(clip);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static Material MaterialAsset(string name, Shader shader, float starAmount, float intensity)
    {
        string path = Folder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetFloat("_StarAmount", starAmount);
        material.SetFloat("_Intensity", intensity);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static AnimationClip AnimationAsset()
    {
        string path = Folder + "/SparkleLoop.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = "SparkleLoop", legacy = true, frameRate = 30 };
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.legacy = true;
        clip.wrapMode = WrapMode.Loop;
        AnimationCurve pulse = AnimationCurve.EaseInOut(0, 1, .8f, 1.06f);
        pulse.AddKey(new Keyframe(1.6f, 1, 0, 0));
        foreach (string axis in new[] { "x", "y", "z" })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Particles", typeof(Transform), "m_LocalScale." + axis), pulse);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static ParticleSystem Particle(Transform parent, string name, Material material, bool diamond)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var system = obj.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.duration = 1.6f;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.useUnscaledTime = true;
        main.maxParticles = 64;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.08f, .18f);
        main.startSize = new ParticleSystem.MinMaxCurve(diamond ? .16f : .1f, diamond ? .28f : .18f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(.28f,.46f,1f,.95f), new Color(.55f,.8f,1f,1f));
        main.stopAction = ParticleSystemStopAction.None;

        var emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = diamond ? 14f : 20f;
        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = .55f;
        shape.radiusThickness = .12f;
        shape.randomDirectionAmount = .35f;

        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
            new Keyframe(0,.25f), new Keyframe(.12f,1), new Keyframe(.72f,.9f), new Keyframe(1,.2f)));
        var color = system.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] {
            new GradientColorKey(new Color(.55f,.8f,1f),0),
            new GradientColorKey(new Color(.45f,.65f,1f),.65f),
            new GradientColorKey(new Color(.3f,.65f,1f),1)
        }, new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,.12f), new GradientAlphaKey(1,.72f), new GradientAlphaKey(0,1) });
        color.color = gradient;
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = new ParticleSystem.MinMaxCurve(.12f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(.4f);
        var noise = system.noise;
        noise.enabled = true;
        noise.strength = .035f;
        noise.frequency = .65f;
        noise.scrollSpeed = .3f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        var rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(.4f);

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX
        });
        renderer.minParticleSize = .004f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return system;
    }
}
#endif
