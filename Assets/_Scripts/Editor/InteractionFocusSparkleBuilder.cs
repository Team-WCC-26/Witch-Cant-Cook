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
        Material stars = MaterialAsset("BlueStar", shader, 1f, 1.4f);
        Material dots = MaterialAsset("BlueGlow", shader, 0f, 1.15f);
        AnimationClip clip = AnimationAsset();

        var root = new GameObject("Interaction Focus Sparkle");
        try
        {
            var pivot = new GameObject("Particles").transform;
            pivot.SetParent(root.transform, false);
            ParticleSystem starSystem = Particle(pivot, "Blue White Stars", stars, true);
            ParticleSystem glowSystem = Particle(pivot, "Blue Violet Glimmer", dots, false);
            var animation = root.AddComponent<Animation>();
            animation.AddClip(clip, clip.name);
            animation.clip = clip;
            animation.playAutomatically = false;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            animation.wrapMode = WrapMode.Loop;
            var effect = root.AddComponent<InteractionFocusSparkleVfx>();
            effect.Configure(new[] { starSystem, glowSystem }, animation, pivot);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
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

    private static ParticleSystem Particle(Transform parent, string name, Material material, bool star)
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
        main.maxParticles = star ? 48 : 64;
        main.startLifetime = new ParticleSystem.MinMaxCurve(star ? .45f : .7f, star ? .85f : 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.015f, .045f);
        main.startSize = new ParticleSystem.MinMaxCurve(star ? .07f : .025f, star ? .14f : .065f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(.28f,.46f,1f,.9f), new Color(.55f,.73f,1f,1f));
        main.stopAction = ParticleSystemStopAction.None;

        var emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = star ? 20f : 32f;
        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = .52f;
        shape.radiusThickness = .04f;
        shape.randomDirectionAmount = .45f;

        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
            new Keyframe(0,0), new Keyframe(.2f,.65f), new Keyframe(.45f,1), new Keyframe(.7f,.55f), new Keyframe(1,0)));
        var color = system.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] {
            new GradientColorKey(new Color(.5f,.72f,1f),0),
            new GradientColorKey(new Color(.48f,.48f,1f),.65f),
            new GradientColorKey(new Color(.3f,.56f,1f),1)
        }, new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,.25f), new GradientAlphaKey(.8f,.6f), new GradientAlphaKey(0,1) });
        color.color = gradient;
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = new ParticleSystem.MinMaxCurve(.025f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(.12f);
        var noise = system.noise;
        noise.enabled = true;
        noise.strength = .018f;
        noise.frequency = .8f;
        noise.scrollSpeed = .25f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        var rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(.12f);

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return system;
    }
}
#endif
