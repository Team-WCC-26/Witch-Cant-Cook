using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IngredientReaction)), CanEditMultipleObjects]
public sealed class IngredientReactionEditor : Editor
{
    #region Unity Callbacks
    private void OnDisable()
    {
        if (Application.isPlaying) return;
        foreach (Object selected in targets)
            if (selected is IngredientReaction reaction) reaction.SetGrillPreview(false);
    }
    #endregion

    public override void OnInspectorGUI()
    {
        bool changed = DrawDefaultInspector();
        IngredientReaction reaction = (IngredientReaction)target;
        if (changed && !Application.isPlaying)
            foreach (Object selected in targets)
                if (selected is IngredientReaction ingredient && ingredient.GrillPreview)
                    ingredient.SetGrillPreview(true);

        using (new EditorGUI.DisabledScope(Application.isPlaying || EditorUtility.IsPersistent(reaction)))
        {
            if (GUILayout.Button(reaction.GrillPreview ? "그릴 미리 보기 끄기" : "그릴 미리 보기"))
            {
                bool preview = !reaction.GrillPreview;
                foreach (Object selected in targets)
                    if (selected is IngredientReaction ingredient) ingredient.SetGrillPreview(preview);
                SceneView.RepaintAll();
            }
        }
    }
}
