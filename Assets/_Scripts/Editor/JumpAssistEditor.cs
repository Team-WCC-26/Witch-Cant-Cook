using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(JumpAssist))]
public class JumpAssistEditor : Editor
{
    private Sequence _preview;
    private Transform _pivot;
    private Quaternion _restRotation;
    private double _startTime;
    private bool _ownsAnimationMode;

    public override void OnInspectorGUI()
    {
        using (new EditorGUI.DisabledScope(_preview != null))
            DrawDefaultInspector();

        if (Application.isPlaying)
        {
            if (GUILayout.Button("모션 실행")) ((JumpAssist)target).PlayMotion();
            return;
        }

        using (new EditorGUI.DisabledScope(_preview == null && AnimationMode.InAnimationMode()))
        {
            if (GUILayout.Button(_preview == null ? "모션 미리 보기" : "미리 보기 중지"))
            {
                if (_preview == null) StartPreview();
                else StopPreview();
            }
        }
    }

    private void StartPreview()
    {
        _pivot = ((JumpAssist)target).transform;
        _restRotation = _pivot.localRotation;
        AnimationMode.StartAnimationMode();
        _ownsAnimationMode = true;

        // Register the temporary rotation so Unity treats it as preview data.
        string[] axes = { "x", "y", "z", "w" };
        for (int i = 0; i < axes.Length; i++)
        {
            string property = "m_LocalRotation." + axes[i];
            AnimationMode.AddPropertyModification(
                EditorCurveBinding.FloatCurve("", typeof(Transform), property),
                new PropertyModification
                {
                    target = _pivot,
                    propertyPath = property,
                    value = _restRotation[i].ToString(System.Globalization.CultureInfo.InvariantCulture)
                }, false);
        }

        _preview = ((JumpAssist)target).CreatePreviewMotion();
        _startTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += UpdatePreview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    private void UpdatePreview()
    {
        if (_pivot == null || _preview == null || !_preview.IsActive()
            || !AnimationMode.InAnimationMode())
        {
            StopPreview();
            return;
        }

        float elapsed = (float)(EditorApplication.timeSinceStartup - _startTime);
        _preview.Goto(elapsed, false);
        SceneView.RepaintAll();
        if (elapsed >= _preview.Duration()) StopPreview();
    }

    private void OnPlayModeChanged(PlayModeStateChange state) => StopPreview();
    private void OnSceneSaving(Scene scene, string path) => StopPreview();
    private void OnDisable() => StopPreview();

    private void StopPreview()
    {
        EditorApplication.update -= UpdatePreview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        _preview?.Kill();
        _preview = null;
        if (_ownsAnimationMode)
        {
            AnimationMode.StopAnimationMode();
            _ownsAnimationMode = false;
            if (_pivot != null) _pivot.localRotation = _restRotation;
        }
        _pivot = null;
        SceneView.RepaintAll();
        Repaint();
    }
}
