using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Audio/Sound SO")]
public sealed class SoundSO : ScriptableObject
{
    [SerializeField] private AudioClip clip;
    [SerializeField] private AudioMixerGroup mixerGroup;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField, Range(0.1f, 3f)] private float pitch = 1f;
    [SerializeField] private bool loop = false;
    [SerializeField] private bool spatialBlend = true;
    [SerializeField, Min(0.01f)] private float minDistance = 1f;
    [SerializeField, Min(0.01f)] private float maxDistance = 20f;

    public AudioClip Clip => clip;

    public void ApplyTo(AudioSource source)
    {
        source.clip = clip;
        source.outputAudioMixerGroup = mixerGroup;
        source.volume = volume;
        source.pitch = pitch;
        source.loop = loop;
        source.spatialBlend = spatialBlend ? 1f : 0f;
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(minDistance, maxDistance);
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
    }

    #region Editor
#if UNITY_EDITOR
    [CustomEditor(typeof(SoundSO))]
    private sealed class SoundSOEditor : Editor
    {
        private PreviewRenderUtility preview;
        private AudioSource previewSource;

        #region Unity Callbacks
        private void OnDisable() => StopPreview();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "spatialBlend", "minDistance", "maxDistance");
            SerializedProperty spatialBlendProperty = serializedObject.FindProperty("spatialBlend");
            spatialBlendProperty.boolValue = EditorGUILayout.Toggle(
                new GUIContent("2D / 3D Sound"),
                spatialBlendProperty.boolValue);

            using (new EditorGUI.DisabledScope(!spatialBlendProperty.boolValue))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("minDistance"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxDistance"));
            }
            bool changed = serializedObject.ApplyModifiedProperties();
            SoundSO sound = (SoundSO)target;
            if (changed && previewSource != null)
            {
                sound.ApplyTo(previewSource);
                previewSource.spatialBlend = 0f;
            }
            using (new EditorGUI.DisabledScope(sound.Clip == null))
            {
                if (GUILayout.Button(previewSource != null ? "미리듣기 정지" : "미리듣기"))
                {
                    if (previewSource != null) StopPreview();
                    else StartPreview(sound);
                }
            }
        }
        #endregion

        private void StartPreview(SoundSO sound)
        {
            preview = new PreviewRenderUtility();
            preview.camera.gameObject.AddComponent<AudioListener>();
            GameObject previewObj = EditorUtility.CreateGameObjectWithHideFlags("Sound Preview", HideFlags.HideAndDontSave, typeof(AudioSource));
            preview.AddSingleGO(previewObj);
            previewSource = previewObj.GetComponent<AudioSource>();
            previewSource.playOnAwake = false;
            sound.ApplyTo(previewSource);
            // Audition settings without scene distance attenuation.
            previewSource.spatialBlend = 0f;
            previewSource.Play();
            EditorApplication.update += UpdatePreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.quitting += StopPreview;
        }

        private void UpdatePreview()
        {
            if (previewSource == null || !previewSource.isPlaying) StopPreview();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state) => StopPreview();

        private void StopPreview()
        {
            EditorApplication.update -= UpdatePreview;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorApplication.quitting -= StopPreview;
            if (previewSource != null) previewSource.Stop();
            preview?.Cleanup();
            preview = null;
            previewSource = null;
            Repaint();
        }
    }
#endif
    #endregion
}