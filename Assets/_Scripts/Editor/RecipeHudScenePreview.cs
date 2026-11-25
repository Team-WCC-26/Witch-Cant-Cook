using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RecipeHudScenePreview
{
    private const string ScenePath = "Assets/_Scenes/RecipeHudCompactPreview.unity";
    private const string HudPath = "Assets/Prefabs/UI/RecipeHud.prefab";
    private const string PreviewName = "Recipe HUD - Linked Preview";
    private const string SlotPrefix = "Preview Ingredient ";
    private const string Sprites = "Assets/Sprites/recipe/";

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
    private static readonly float[] Time = { .2f, .47f, .74f };
    private static readonly Color32[] TimerColors =
    {
        new Color32(240, 105, 83, 255),
        new Color32(255, 192, 87, 255),
        new Color32(146, 219, 111, 255)
    };

    private static GameObject renderedHud;
    private static Hash128 renderedLayout;

    static RecipeHudScenePreview()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.projectChanged += QueueRefresh;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        QueueRefresh();
    }

    [MenuItem("Tools/Recipe HUD/Show Scene Preview")]
    private static void ShowScenePreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null)
        {
            if (prefabStage.scene.isDirty)
            {
                Debug.LogWarning("레시피 HUD 미리보기를 열기 전에 현재 프리팹 변경을 저장하세요.");
                return;
            }

            StageUtility.GoToMainStage();
        }

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath);
        }

        GameObject hud = FindHud(scene);
        if (hud == null) return;
        DrawExamples(hud);

        var cards = (RectTransform)hud.transform.Find("Cards");
        var corners = new Vector3[4];
        cards.GetWorldCorners(corners);
        var bounds = new Bounds(corners[0], Vector3.zero);
        foreach (Vector3 corner in corners) bounds.Encapsulate(corner);
        bounds.Expand(bounds.size.magnitude * .15f);

        var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
        view.in2DMode = true;
        view.Frame(bounds, true);
        view.Focus();
        Selection.activeGameObject = cards.gameObject;
        view.Repaint();
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == ScenePath) QueueRefresh();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode) QueueRefresh();
    }

    private static void QueueRefresh()
    {
        EditorApplication.delayCall -= RefreshIfChanged;
        EditorApplication.delayCall += RefreshIfChanged;
    }

    private static void RefreshIfChanged()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        GameObject hud = FindHud(SceneManager.GetSceneByPath(ScenePath));
        if (hud == null) return;

        Hash128 layout = AssetDatabase.GetAssetDependencyHash(HudPath);
        if (hud == renderedHud && layout == renderedLayout) return;
        DrawExamples(hud);
    }

    private static GameObject FindHud(Scene scene)
    {
        if (!scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform preview = root.transform.Find(PreviewName);
            if (preview != null) return preview.gameObject;
        }

        return null;
    }

    private static Sprite LoadSprite(string folder, string name)
    {
        return string.IsNullOrEmpty(name)
            ? null
            : AssetDatabase.LoadAssetAtPath<Sprite>(Sprites + folder + "/" + name + "_Icon.png");
    }

    internal static void DrawExamples(GameObject hud)
    {
        var cards = (RectTransform)hud.transform.Find("Cards");
        for (int i = 0; i < Dishes.Length && i < cards.childCount; i++)
        {
            Transform card = cards.GetChild(i);
            card.gameObject.SetActive(true);
            card.Find("Dish/Dish Icon").GetComponent<Image>().sprite = LoadSprite("Recipe_Icon", Dishes[i]);

            Transform ingredients = card.Find("Ingredients");
            for (int j = ingredients.childCount - 1; j >= 0; j--)
            {
                GameObject oldSlot = ingredients.GetChild(j).gameObject;
                if (oldSlot.name.StartsWith(SlotPrefix, System.StringComparison.Ordinal)
                    && (oldSlot.hideFlags & HideFlags.DontSaveInEditor) != 0)
                    Object.DestroyImmediate(oldSlot);
            }

            Transform template = card.Find("Ingredient Template");
            for (int j = 0; j < Ingredients[i].Length; j++)
            {
                Transform slot = Object.Instantiate(template, ingredients, false);
                slot.name = SlotPrefix + (j + 1);
                foreach (Transform child in slot.GetComponentsInChildren<Transform>(true))
                    child.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

                slot.Find("Ingredient").GetComponent<Image>().sprite = LoadSprite("ingredient_Icon", Ingredients[i][j]);
                Image method = slot.Find("Method Icons/Method 0").GetComponent<Image>();
                method.sprite = LoadSprite("Cooking_Icon", Methods[i][j]);
                method.gameObject.SetActive(method.sprite != null);
                slot.gameObject.SetActive(true);
            }

            var timer = card.Find("Timer").GetComponent<RecipeTimerRing>();
            timer.FillAmount = Time[i];
            timer.color = TimerColors[i];
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(cards);
        Canvas.ForceUpdateCanvases();
        SceneView.RepaintAll();
        if (hud.name == PreviewName)
        {
            renderedHud = hud;
            renderedLayout = AssetDatabase.GetAssetDependencyHash(HudPath);
        }
    }
}

[InitializeOnLoad]
internal static class StageRecipeHudScenePreview
{
    private const string StagePath = "Assets/Addressables/UIPrefabs/StageHudUI.prefab";
    private const HideFlags Temporary = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor
        | HideFlags.DontSaveInBuild | HideFlags.NotEditable;

    private static readonly System.Collections.Generic.Dictionary<CanvasRenderer, bool> OriginalCulling = new();
    private static GameObject previewRoot;
    private static GameObject previewHud;

    static StageRecipeHudScenePreview()
    {
        PrefabStage.prefabStageOpened += OnPrefabStageOpened;
        PrefabStage.prefabStageClosing += OnPrefabStageClosing;
        Undo.postprocessModifications += OnInspectorChanges;
        Undo.undoRedoPerformed += QueueRefresh;
        EditorApplication.projectChanged += QueueRefresh;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += ClearPreview;
        QueueRefresh();
    }

    [MenuItem("Tools/Recipe HUD/Show Stage HUD Preview")]
    private static void ShowPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.assetPath != StagePath)
        {
            if (stage != null && stage.scene.isDirty)
            {
                Debug.LogWarning("레시피 HUD 미리보기를 열기 전에 현재 프리팹 변경을 저장하세요.");
                return;
            }

            stage = PrefabStageUtility.OpenPrefab(StagePath);
        }

        RefreshPreview();
        if (previewHud == null) return;

        var cards = (RectTransform)previewHud.transform.Find("Cards");
        var corners = new Vector3[4];
        cards.GetWorldCorners(corners);
        var bounds = new Bounds(corners[0], Vector3.zero);
        foreach (Vector3 corner in corners) bounds.Encapsulate(corner);
        bounds.Expand(bounds.size.magnitude * .1f);

        var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
        view.in2DMode = true;
        view.Frame(bounds, true);
        view.Focus();
        Selection.activeTransform = stage.prefabContentsRoot.transform.Find("Recipe HUD/Cards");
        view.Repaint();
    }

    private static void OnPrefabStageOpened(PrefabStage stage)
    {
        if (stage.assetPath == StagePath) QueueRefresh();
    }

    private static void OnPrefabStageClosing(PrefabStage stage)
    {
        if (stage.assetPath == StagePath) ClearPreview();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode) ClearPreview();
        if (change == PlayModeStateChange.EnteredEditMode) QueueRefresh();
    }

    private static UndoPropertyModification[] OnInspectorChanges(UndoPropertyModification[] modifications)
    {
        if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == StagePath) QueueRefresh();
        return modifications;
    }

    private static void QueueRefresh()
    {
        EditorApplication.delayCall -= RefreshPreview;
        EditorApplication.delayCall += RefreshPreview;
    }

    private static void RefreshPreview()
    {
        ClearPreview();
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.assetPath != StagePath) return;

        var stageRoot = stage.prefabContentsRoot.transform as RectTransform;
        var sourceHud = stageRoot?.Find("Recipe HUD") as RectTransform;
        if (stageRoot == null || sourceHud == null || stageRoot.parent == null || !sourceHud.gameObject.activeInHierarchy)
            return;

        // Sibling under the editor's environment canvas, outside the prefab contents saved by Unity.
        previewRoot = new GameObject("Recipe HUD editor preview", typeof(RectTransform)) { hideFlags = Temporary };
        SceneManager.MoveGameObjectToScene(previewRoot, stage.scene);
        var host = (RectTransform)previewRoot.transform;
        host.SetParent(stageRoot.parent, false);
        CopyRect(stageRoot, host);

        var display = Object.Instantiate(sourceHud, host, false);
        previewHud = display.gameObject;
        foreach (Transform child in display.GetComponentsInChildren<Transform>(true))
            child.gameObject.hideFlags = Temporary;
        CopyRect(sourceHud, display);
        RecipeHudScenePreview.DrawExamples(previewHud);

        // Suppress only the live renderers in this editor stage; cull is not serialized.
        foreach (CanvasRenderer renderer in sourceHud.GetComponentsInChildren<CanvasRenderer>(true))
        {
            OriginalCulling[renderer] = renderer.cull;
            renderer.cull = true;
        }
        SceneView.RepaintAll();
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.anchoredPosition3D = source.anchoredPosition3D;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static void ClearPreview()
    {
        foreach (var pair in OriginalCulling)
            if (pair.Key != null) pair.Key.cull = pair.Value;
        OriginalCulling.Clear();
        if (previewRoot != null) Object.DestroyImmediate(previewRoot);
        previewRoot = null;
        previewHud = null;
    }
}
