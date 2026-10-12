#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Produces editable scene objects and wires all slots and both physical page controls.
public static class StageWorldCanvasBuilder
{
    [MenuItem("GameObject/UI/Stage Selection World Canvas", false, 10)]
    public static void CreateBoard()
    {
        Transform parent = Selection.activeTransform;
        var board = new GameObject("Stage Selection Board");
        Undo.RegisterCreatedObjectUndo(board, "Create Stage Selection Board");
        if (parent != null) board.transform.SetParent(parent, false);
        var controller = board.AddComponent<StageSelectionController>();

        RectTransform root = Rect("World Canvas", board.transform, 0, 0, 900, 500);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f;
        root.localPosition = new Vector3(0, 1.3f, 0);
        root.localScale = Vector3.one * .004f;
        Canvas canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        Background(root, new Color(.82f, .82f, .80f));
        var view = root.gameObject.AddComponent<StageSelectionWorldUI>();
        TMP_Text title = Text("Stage Title", root, 20, 14, 860, 42, "스테이지 1", 30, true);

        Text("Ingredients Heading", root, 24, 70, 520, 32, "재료", 25);
        RectTransform ingredientPanel = Panel("Ingredients", root, 20, 108, 564, 264);
        RectTransform divider = Rect("Column Divider", ingredientPanel, 281, 12, 1, 240);
        Background(divider, new Color(.65f, .65f, .65f));
        var ingredients = new StageBoardItemUI[6];
        for (int i = 0; i < 6; i++)
            ingredients[i] = Item(ingredientPanel, "Ingredient " + (i + 1),
                12 + (i / 3) * 282, 8 + (i % 3) * 82, 258, 76);

        Text("Events Heading", root, 610, 70, 270, 32, "이벤트", 25);
        RectTransform eventPanel = Panel("Events", root, 604, 108, 276, 264);
        var events = new StageBoardItemUI[3];
        for (int i = 0; i < 3; i++)
            events[i] = Item(eventPanel, "Event " + (i + 1), 12, 8 + i * 82, 252, 76);

        TMP_Text previous = Text("Previous Arrow", root, 20, 402, 46, 60, "◀", 34, true);
        RectTransform tipPanel = Panel("Tip", root, 78, 390, 454, 76);
        Text("Tip Heading", tipPanel, 10, 4, 420, 20, "TIP", 18);
        TMP_Text tip = Text("Tip Text", tipPanel, 10, 26, 434, 46, "", 18);
        RectTransform goalPanel = Panel("Goal", root, 550, 390, 272, 76);
        Text("Goal Heading", goalPanel, 10, 4, 252, 20, "목표", 18);
        TMP_Text goal = Text("Goal Text", goalPanel, 10, 26, 252, 46, "", 18);
        TMP_Text next = Text("Next Arrow", root, 834, 402, 46, 60, "▶", 34, true);
        TMP_Text status = Text("Status", root, 20, 474, 860, 22, "", 15, true);
        view.Configure(title, tip, goal, status, previous, next, ingredients, events);
        controller.Configure(view);
        Cylinder(board.transform, controller, "Previous Page Cylinder", -1.35f, StagePageCylinder.PageDirection.Previous);
        Cylinder(board.transform, controller, "Next Page Cylinder", 1.35f, StagePageCylinder.PageDirection.Next);
        EditorSceneManager.MarkSceneDirty(board.scene);
        Selection.activeGameObject = board;
    }

    private static void Cylinder(Transform parent, StageSelectionController controller, string name,
        float x, StagePageCylinder.PageDirection direction)
    {
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = name;
        cylinder.transform.SetParent(parent, false);
        cylinder.transform.localPosition = new Vector3(x, -.12f, 0);
        cylinder.transform.localRotation = Quaternion.Euler(90, 0, 0);
        cylinder.transform.localScale = new Vector3(.55f, .12f, .55f);
        // Included in PlayerBrain's default interaction mask (layers 7 and 10).
        cylinder.layer = 10;
        cylinder.AddComponent<StagePageCylinder>().Configure(controller, direction);
    }

    private static StageBoardItemUI Item(Transform parent, string name, float x, float y, float width, float height)
    {
        RectTransform item = Rect(name, parent, x, y, width, height);
        var view = item.gameObject.AddComponent<StageBoardItemUI>();
        RectTransform iconBackground = Rect("Icon Background", item, 0, 5, 60, 60);
        Background(iconBackground, new Color(.84f, .84f, .84f));
        RectTransform iconRect = Rect("Icon", iconBackground, 0, 0, 60, 60);
        Image icon = iconRect.gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.enabled = false;
        TMP_Text title = Text("Name", item, 70, 5, width - 74, 26, "", 20);
        TMP_Text details = Text("Description", item, 70, 33, width - 74, 38, "", 15);
        view.Configure(icon, title, details);
        return view;
    }

    private static RectTransform Panel(string name, Transform parent, float x, float y, float w, float h)
    {
        RectTransform panel = Rect(name, parent, x, y, w, h);
        Background(panel, Color.white);
        return panel;
    }

    private static void Background(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static TMP_Text Text(string name, Transform parent, float x, float y, float w, float h,
        string value, float size, bool centered = false)
    {
        var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = size;
        label.color = Color.black;
        label.text = value;
        label.alignment = centered ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.richText = false;
        return label;
    }
}
#endif
