using UnityEngine;

public static class IngredientCutVfx
{
    private const string PrefabPath = "VFX/IngredientCutBurst";
    private static ParticleSystem prefab;

    public static Color ResolveColor(Ingredient ingredient)
    {
        if (ingredient != null &&
            ColorUtility.TryParseHtmlString(ingredient.particleColor, out Color color))
            return color;

        return new Color(0.95f, 0.85f, 0.65f);
    }

    public static void PlayAt(Vector3 position, Ingredient ingredient)
    {
        if (prefab == null)
            prefab = Resources.Load<ParticleSystem>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"Cut particle prefab not found: Resources/{PrefabPath}");
            return;
        }

        ParticleSystem instance = Object.Instantiate(prefab, position, Quaternion.identity);
        Play(instance, ResolveColor(ingredient));

        ParticleSystem.MainModule main = instance.main;
        Object.Destroy(instance.gameObject, main.duration + main.startLifetime.constantMax + 0.1f);
    }

    public static void Play(ParticleSystem particle, Color color)
    {
        if (particle == null) return;

        ParticleSystem.MainModule main = particle.main;
        main.startColor = color;
        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particle.Play(true);
    }
}
