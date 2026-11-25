using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[CustomEditor(typeof(RecipeOrderCardUI))]
internal sealed class RecipeCardPreviewEditor : UnityEditor.Editor
{
    private const string CardPath = "Assets/Prefabs/UI/RecipeCardCompact.prefab";

    public override void OnInspectorGUI()
    {
        var card = (RecipeOrderCardUI)target;
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == CardPath && stage.prefabContentsRoot == card.gameObject)
        {
            List<RecipeIngredientUI> slots = GetSlots(card);
            if (slots.Count > 0)
            {
                int count = slots.FindAll(slot => slot.gameObject.activeSelf).Count;
                EditorGUILayout.LabelField($"미리보기 재료 {count} / {slots.Count}", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(count <= 1))
                        if (GUILayout.Button("재료 빼기")) SetPreviewCount(card, count - 1);
                    using (new EditorGUI.DisabledScope(count >= slots.Count))
                        if (GUILayout.Button("재료 추가")) SetPreviewCount(card, count + 1);
                }
                EditorGUILayout.Space();
            }
        }

        DrawDefaultInspector();
    }

    internal static List<RecipeIngredientUI> GetSlots(RecipeOrderCardUI card)
    {
        var result = new List<RecipeIngredientUI>();
        var entries = new SerializedObject(card).FindProperty("authoredIngredientViews");
        for (int i = 0; i < entries.arraySize; i++)
        {
            if (entries.GetArrayElementAtIndex(i).objectReferenceValue is RecipeIngredientUI slot)
                result.Add(slot);
        }
        return result;
    }

    internal static void SetPreviewCount(RecipeOrderCardUI card, int requestedCount)
    {
        List<RecipeIngredientUI> slots = GetSlots(card);
        if (slots.Count == 0) return;

        int count = Mathf.Clamp(requestedCount, 1, slots.Count);
        var changing = new List<Object>();
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].gameObject.activeSelf != (i < count))
                changing.Add(slots[i].gameObject);
        if (changing.Count == 0) return;

        Undo.RecordObjects(changing.ToArray(), "Change recipe ingredient preview count");
        for (int i = 0; i < slots.Count; i++)
        {
            GameObject slot = slots[i].gameObject;
            if (slot.activeSelf == (i < count)) continue;
            slot.SetActive(i < count);
            PrefabUtility.RecordPrefabInstancePropertyModifications(slot);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card.transform);
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(card.gameObject.scene);
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Recipe HUD/Open Recipe Card Preview")]
    private static void OpenCardPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.assetPath != CardPath)
            stage = PrefabStageUtility.OpenPrefab(CardPath);

        if (stage == null || stage.assetPath != CardPath) return;
        GameObject root = stage.prefabContentsRoot;
        Selection.activeGameObject = root;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
        Canvas.ForceUpdateCanvases();
        var sceneView = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
        sceneView.in2DMode = true;
        sceneView.FrameSelected();
        sceneView.Focus();
    }
}

// Keeps the older standalone comparison scene working with the authored slots.
[InitializeOnLoad]
internal static class RecipeHudScenePreview
{
    private const string ScenePath = "Assets/_Scenes/RecipeHudCompactPreview.unity";

    private static readonly string[] Dishes = { "MushroomSkewer", "Gratin", "RocketOmelette" };
    private static readonly string[][] Ingredients =
    {
        new[] { "Mushroom" },
        new[] { "Potato", "Cheese" },
        new[] { "Egg", "Carrot", "Onion" }
    };

    private static readonly string[][] Methods =
    {
        new[] { "Cutted" },
        new[] { "Cutted", null },
        new[] { "Pan", "Cutted", "Cutted" }
    };

    static RecipeHudScenePreview()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == ScenePath) EditorApplication.delayCall += RefreshComparison;
    }

    [MenuItem("Tools/Recipe HUD/Show Scene Preview")]
    private static void OpenComparison()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        StageUtility.GoToMainStage();
        RefreshComparison();
        var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
        view.Focus();
    }

    private static void RefreshComparison()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform cards = root.transform.Find("Recipe HUD - Linked Preview/Cards");
            if (cards == null) continue;
            for (int i = 0; i < cards.childCount && i < Dishes.Length; i++)
            {
                Transform card = cards.GetChild(i);
                card.Find("Dish/Dish Icon").GetComponent<Image>().sprite = FindSprite("Recipe_Icon", Dishes[i]);
                List<RecipeIngredientUI> slots = RecipeCardPreviewEditor.GetSlots(card.GetComponent<RecipeOrderCardUI>());
                for (int j = 0; j < slots.Count; j++)
                {
                    bool visible = j < Ingredients[i].Length;
                    slots[j].gameObject.SetActive(visible);
                    if (!visible) continue;
                    Transform slot = slots[j].transform;
                    slot.Find("Ingredient").GetComponent<Image>().sprite = FindSprite("ingredient_Icon", Ingredients[i][j]);
                    Image method = slot.Find("Method Icons/Method 0").GetComponent<Image>();
                    method.sprite = FindSprite("Cooking_Icon", Methods[i][j]);
                    method.gameObject.SetActive(method.sprite != null);
                }
                card.GetComponentInChildren<RecipeTimerRing>().FillAmount = (i + 1) / 4f;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)cards);
        }
        Canvas.ForceUpdateCanvases();
        SceneView.RepaintAll();
    }

    private static Sprite FindSprite(string folder, string name)
    {
        return string.IsNullOrEmpty(name) ? null
            : AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Sprites/recipe/{folder}/{name}_Icon.png");
    }
}
