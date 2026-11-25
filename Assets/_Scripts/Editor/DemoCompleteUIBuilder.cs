using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DemoCompleteUIBuilder
{
    public const string PrefabPath = "Assets/_Scripts/Resources/UI/DemoCompleteUI.prefab";
    private const string FontPath = "Assets/TextMesh Pro/Fonts/Maplestory Light SDF.asset";

    [MenuItem("Tools/UI/Rebuild Demo Complete Prefab")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Scripts/Resources"))
            AssetDatabase.CreateFolder("Assets/_Scripts", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/_Scripts/Resources/UI"))
            AssetDatabase.CreateFolder("Assets/_Scripts/Resources", "UI");

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) throw new System.InvalidOperationException("Demo completion requires the project's Korean font.");

        // Author as a nested Canvas so Unity does not bake a root Canvas's driven zero-size transform.
        var authoringCanvas = new GameObject("DemoCompleteAuthoringCanvas", typeof(RectTransform), typeof(Canvas));
        authoringCanvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var root = new GameObject("DemoCompleteUI", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasGroup), typeof(GraphicRaycaster));
        root.transform.SetParent(authoringCanvas.transform, false);
        root.layer = LayerMask.NameToLayer("UI");
        try
        {
            Stretch((RectTransform)root.transform);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30000;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var shade = new GameObject("BlackFade", typeof(RectTransform), typeof(Image));
            shade.layer = root.layer;
            shade.transform.SetParent(root.transform, false);
            Stretch((RectTransform)shade.transform);
            Image image = shade.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, .78f);
            image.raycastTarget = true;

            var textObject = new GameObject("DemoCompleteText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.layer = root.layer;
            textObject.transform.SetParent(root.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = new Vector2(.06f, .38f);
            textRect.anchorMax = new Vector2(.94f, .62f);
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = "데모버전은 여기까지 입니다";
            text.fontSize = 90f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 32f;
            text.fontSizeMax = 90f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Color.white;
            text.raycastTarget = false;

            DemoCompleteUI controller = root.AddComponent<DemoCompleteUI>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("fadeGroup").objectReferenceValue = group;
            serialized.FindProperty("fadeDuration").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("Demo completion prefab saved: " + PrefabPath);
        }
        finally { Object.DestroyImmediate(authoringCanvas); }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(.5f, .5f);
        rect.localScale = Vector3.one;
    }
}
