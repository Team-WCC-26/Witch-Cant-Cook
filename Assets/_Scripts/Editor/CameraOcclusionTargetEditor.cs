#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(CameraOcclusionTarget)), CanEditMultipleObjects]
public sealed class CameraOcclusionTargetEditor : Editor
{
    private readonly BoxBoundsHandle boxHandle = new();

    #region Unity Callbacks

    /// <summary>Draws target settings and the renderer configuration button.</summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (Application.isPlaying) return;
        if (!GUILayout.Button("Configure Renderers")) return;
        foreach (Object selectedTarget in targets)
        {
            var occlusionTarget = (CameraOcclusionTarget)selectedTarget;
            occlusionTarget.ConfigureRenderers();
            CameraOcclusionSetup.ConfigureTargetMaterials(occlusionTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(occlusionTarget);
        }
    }

    private void OnSceneGUI()
    {
        if (Application.isPlaying || targets.Length != 1) return;
        serializedObject.Update();
        SerializedProperty boxes = serializedObject.FindProperty("detectionBoxes");
        var occlusionTarget = (CameraOcclusionTarget)target;
        for (int index = 0; index < boxes.arraySize; index++)
        {
            SerializedProperty box = boxes.GetArrayElementAtIndex(index);
            Transform space = box.FindPropertyRelative("space").objectReferenceValue as Transform;
            if (space == null) space = occlusionTarget.transform;
            SerializedProperty boundsProperty = box.FindPropertyRelative("bounds");
            Bounds bounds = boundsProperty.boundsValue;
            using (new Handles.DrawingScope(new Color(0.2f, 0.8f, 1f), space.localToWorldMatrix))
            {
                boxHandle.center = bounds.center;
                boxHandle.size = bounds.size;
                EditorGUI.BeginChangeCheck();
                boxHandle.DrawHandle();
                if (!EditorGUI.EndChangeCheck()) continue;
                Undo.RecordObject(occlusionTarget, "Resize Camera Occlusion Detection Box");
                boundsProperty.boundsValue = new Bounds(boxHandle.center, boxHandle.size);
            }
        }
        serializedObject.ApplyModifiedProperties();
    }

    #endregion
}
#endif
