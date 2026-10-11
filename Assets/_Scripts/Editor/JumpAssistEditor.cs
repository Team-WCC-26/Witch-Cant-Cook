using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(JumpAssist))]
public class JumpAssistEditor : Editor
{
    private Sequence preview;
    private Transform pivot;
    private Quaternion restRotation;
    private double startTime;
    private bool ownsAnimationMode;

    #region Unity Callbacks

    private void OnDisable() => StopPreview();
    private void OnPlayModeChanged(PlayModeStateChange state) => StopPreview();
    private void OnSceneSaving(Scene scene, string path) => StopPreview();

    public override void OnInspectorGUI()
    {
        using (new EditorGUI.DisabledScope(preview != null))
            DrawDefaultInspector();

        if (Application.isPlaying)
        {
            if (GUILayout.Button("모션 실행")) ((JumpAssist)target).PlayMotion();
            return;
        }

        using (new EditorGUI.DisabledScope(EditorUtility.IsPersistent(target)
            || (preview == null && AnimationMode.InAnimationMode())))
        {
            if (GUILayout.Button(preview == null ? "모션 미리 보기" : "미리 보기 중지"))
            {
                if (preview == null) StartPreview();
                else StopPreview();
            }
        }
    }

    #endregion

    #region Editor

    private void StartPreview()
    {
        pivot = ((JumpAssist)target).transform;
        restRotation = pivot.localRotation;
        AnimationMode.StartAnimationMode();
        ownsAnimationMode = true;

        // Register the temporary rotation so Unity treats it as preview data.
        string[] axes = { "x", "y", "z", "w" };
        for (int i = 0; i < axes.Length; i++)
        {
            string property = "m_LocalRotation." + axes[i];
            AnimationMode.AddPropertyModification(
                EditorCurveBinding.FloatCurve("", typeof(Transform), property),
                new PropertyModification
                {
                    target = pivot,
                    propertyPath = property,
                    value = restRotation[i].ToString(System.Globalization.CultureInfo.InvariantCulture)
                }, false);
        }

        preview = ((JumpAssist)target).CreatePreviewMotion();
        startTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += UpdatePreview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    private void UpdatePreview()
    {
        if (pivot == null || preview == null || !preview.IsActive()
            || !AnimationMode.InAnimationMode())
        {
            StopPreview();
            return;
        }

        float elapsed = (float)(EditorApplication.timeSinceStartup - startTime);
        preview.Goto(elapsed, false);
        SceneView.RepaintAll();
        if (elapsed >= preview.Duration()) StopPreview();
    }


    private void StopPreview()
    {
        EditorApplication.update -= UpdatePreview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        preview?.Kill();
        preview = null;
        if (ownsAnimationMode)
        {
            AnimationMode.StopAnimationMode();
            ownsAnimationMode = false;
            if (pivot != null) pivot.localRotation = restRotation;
        }
        pivot = null;
        SceneView.RepaintAll();
        Repaint();
    }

    #endregion
}
