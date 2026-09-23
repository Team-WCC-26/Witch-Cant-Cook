#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Creates editable scene UI without requiring hand-wiring every card field.
public static class RecipeWorldCanvasBuilder
{
    [MenuItem("GameObject/UI/Recipe World Canvas (3 Orders)", false, 10)]
    private static void CreateBoard()
    {
        Transform parent = Selection.activeTransform;
        GameObject board = new GameObject("Recipe World Canvas", typeof(RectTransform), typeof(Canvas));
        Undo.RegisterCreatedObjectUndo(board, "Create Recipe World Canvas");
        if (parent != null) board.transform.SetParent(parent, false);
        Canvas canvas = board.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform root = board.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(900, 820);
        root.localScale = Vector3.one * .001f;
        RecipeScrollView view = board.AddComponent<RecipeScrollView>();
        RectTransform content = Rect("Cards", root, 0, 0, 900, 820);
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 16;
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        RecipeOrderCardUI[] cards = new RecipeOrderCardUI[3];
        for (int i = 0; i < cards.Length; i++) cards[i] = CreateCard(content, i);
        view.Configure(content, cards);
        Selection.activeGameObject = board;
    }

    private static RecipeOrderCardUI CreateCard(Transform parent, int index)
    {
        RectTransform rect = Rect("Recipe Card " + (index + 1), parent, 0, 0, 900, 260);
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(.83f, .81f, .75f, .95f); background.raycastTarget = false;
        LayoutElement size = rect.gameObject.AddComponent<LayoutElement>();
        size.preferredHeight = 260; size.minHeight = 260;
        RecipeOrderCardUI card = rect.gameObject.AddComponent<RecipeOrderCardUI>();
        Image dish = Picture("Dish", rect, 16, 16, 140, 132);
        Text("Final Method Arrow", rect, 62, 148, 44, 26, "↑", 24);
        TMP_Text title = Text("Dish Name", rect, 178, 14, 600, 44, "요리 이름", 30);
        Text("Final Method Label", rect, 16, 230, 146, 22, "최종 조리", 16);
        BuildMethods(rect, 16, 174, 36, out Image[] finalMethods, out TMP_Text[] finalLabels);
        RectTransform timerRect = Rect("Timer", rect, 800, 14, 76, 76);
        RecipeTimerRing ring = timerRect.gameObject.AddComponent<RecipeTimerRing>();
        ring.color = Color.green; ring.raycastTarget = false;
        TMP_Text time = Text("Seconds", timerRect, 8, 22, 60, 30, "60", 20);
        RectTransform ingredients = Rect("Ingredients", rect, 178, 88, 700, 156);
        HorizontalLayoutGroup row = ingredients.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 10; row.childAlignment = TextAnchor.UpperLeft;
        row.childControlWidth = true; row.childControlHeight = true;
        row.childForceExpandWidth = false; row.childForceExpandHeight = false;
        RecipeIngredientUI template = CreateIngredient(rect);
        template.gameObject.SetActive(false);
        card.Configure(title, dish, ring, time, finalMethods, finalLabels, template, ingredients);
        return card;
    }

    private static RecipeIngredientUI CreateIngredient(Transform parent)
    {
        RectTransform rect = Rect("Ingredient Template", parent, 0, 0, 156, 156);
        LayoutElement size = rect.gameObject.AddComponent<LayoutElement>();
        size.preferredWidth = 156; size.flexibleWidth = 0; size.minWidth = 0;
        size.preferredHeight = 156;
        RecipeIngredientUI view = rect.gameObject.AddComponent<RecipeIngredientUI>();
        Image image = Picture("Ingredient", rect, 30, 0, 96, 96);
        TMP_Text name = Text("Name", rect, 0, 96, 156, 24, "재료", 18);
        BuildMethods(rect, 0, 122, 36, out Image[] methods, out TMP_Text[] labels);
        view.Configure(image, name, methods, labels);
        return view;
    }

    private static void BuildMethods(Transform parent, float x, float y, float size, out Image[] images, out TMP_Text[] labels)
    {
        images = new Image[4]; labels = new TMP_Text[4];
        for (int i = 0; i < 4; i++)
        {
            images[i] = Picture("Method " + i, parent, x + i * size, y, size - 2, size - 2);
            labels[i] = Text("Method Text " + i, parent, x + i * size, y, size - 2, size - 2, "", 12);
        }
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
    private static Image Picture(string name, Transform parent, float x, float y, float w, float h)
    {
        Image image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
        image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }
    private static TMP_Text Text(string name, Transform parent, float x, float y, float w, float h, string value, float size)
    {
        TMP_Text text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset; text.text = value; text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center; text.color = Color.black; text.raycastTarget = false;
        return text;
    }
}
#endif

