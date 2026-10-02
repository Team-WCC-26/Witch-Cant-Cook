using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DishCompleteVfx))]
public sealed class DishCompleteVfxEditor : Editor
{
    private float sampleTime;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var effect = (DishCompleteVfx)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("편집 모드에서 재생할 수 있습니다. 별빛은 루트의 파티클 시스템, 잔광과 고리는 자식 오브젝트에서 조절하세요.", MessageType.Info);
        bool persistent = EditorUtility.IsPersistent(effect);
        using (new EditorGUI.DisabledScope(persistent))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("한 번 미리보기"))
                {
                    if (Application.isPlaying) effect.PlayOnce();
                    else DishCompleteVfxPreview.PlayPreview(effect, false);
                }
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (GUILayout.Button("반복 미리보기"))
                        DishCompleteVfxPreview.PlayPreview(effect, true);
                if (GUILayout.Button("정지"))
                {
                    DishCompleteVfxPreview.StopPreview();
                    effect.StopEffect();
                }
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUI.BeginChangeCheck();
                sampleTime = EditorGUILayout.Slider("미리보기 시간(초)", sampleTime, 0f, DishCompleteVfxPreview.GetDuration(effect));
                if (EditorGUI.EndChangeCheck())
                {
                    DishCompleteVfxPreview.StopPreview();
                    DishCompleteVfxPreview.Sample(effect, sampleTime);
                }
            }
        }
        if (persistent)
            EditorGUILayout.HelpBox("씬의 미리보기 오브젝트를 선택하거나 프리팹을 더블클릭해서 연 뒤 재생하세요.", MessageType.Info);
    }
}

[InitializeOnLoad]
public static class DishCompleteVfxPreview
{
    private static DishCompleteVfx current;
    private static double startedAt;
    private static bool repeat;

    static DishCompleteVfxPreview()
    {
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorApplication.playModeStateChanged += _ => StopPreview();
        EditorApplication.quitting += StopPreview;
    }

    public static float GetDuration(DishCompleteVfx effect)
    {
        float duration = 0.1f;
        foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = system.main;
            duration = Mathf.Max(duration, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
        }
        return duration + 0.15f;
    }

    public static void PlayPreview(DishCompleteVfx effect, bool loop)
    {
        StopPreview();
        if (effect == null || EditorUtility.IsPersistent(effect) || Application.isPlaying) return;
        current = effect;
        repeat = loop;
        startedAt = EditorApplication.timeSinceStartup;
        Sample(current, 0.001f);
        EditorApplication.update += Tick;
    }

    public static void Sample(DishCompleteVfx effect, float seconds)
    {
        if (effect == null || EditorUtility.IsPersistent(effect)) return;
        effect.Root.Simulate(Mathf.Max(0.001f, seconds), true, true, true);
        SceneView.RepaintAll();
    }

    private static void Tick()
    {
        if (current == null || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            StopPreview();
            return;
        }
        float duration = GetDuration(current);
        float elapsed = (float)(EditorApplication.timeSinceStartup - startedAt);
        if (repeat) elapsed %= duration + 0.4f;
        if (elapsed >= duration)
        {
            if (!repeat) StopPreview();
            else current.Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        else Sample(current, elapsed);
    }

    public static void StopPreview()
    {
        EditorApplication.update -= Tick;
        if (current != null)
            current.Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        current = null;
        SceneView.RepaintAll();
    }
}
