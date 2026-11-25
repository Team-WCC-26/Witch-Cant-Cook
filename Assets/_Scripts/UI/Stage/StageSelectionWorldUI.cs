using System;
using TMPro;
using UnityEngine;

public class StageSelectionWorldUI : MonoBehaviour
{
    [SerializeField] private TMP_Text stageTitle;
    [SerializeField] private TMP_Text tipText;
    [SerializeField] private TMP_Text goalText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text previousArrow;
    [SerializeField] private TMP_Text nextArrow;
    [SerializeField] private StagePageCylinder previousTarget;
    [SerializeField] private StagePageCylinder nextTarget;

    [Tooltip("왼쪽 열 위에서 아래 3개, 오른쪽 열 위에서 아래 3개 순서")]
    [SerializeField] private StageBoardItemUI[] ingredients = Array.Empty<StageBoardItemUI>();
    [SerializeField] private StageBoardItemUI[] events = Array.Empty<StageBoardItemUI>();

    public void Configure(TMP_Text title, TMP_Text tip, TMP_Text goal, TMP_Text status,
        StageBoardItemUI[] ingredientSlots, StageBoardItemUI[] eventSlots,
        TMP_Text previous = null, TMP_Text next = null)
    {
        stageTitle = title; tipText = tip; goalText = goal; statusText = status;
        ingredients = ingredientSlots; events = eventSlots;
        previousArrow = previous;
        nextArrow = next;
        SetPageControls(false, false);
    }

    internal void BindPageTargets(StageSelectionController controller)
    {
        BindPageTarget(previousTarget, previousArrow, controller, -1);
        BindPageTarget(nextTarget, nextArrow, controller, 1);
    }

    private static void BindPageTarget(StagePageCylinder target, TMP_Text arrow,
        StageSelectionController controller, int direction)
    {
        if (target == null)
        {
            if (arrow == null) return;
            GameObject arrowObject = arrow.gameObject;
            arrowObject.layer = LayerMask.NameToLayer("Interactable");
            if (!arrow.TryGetComponent(out BoxCollider hitbox))
                hitbox = arrowObject.AddComponent<BoxCollider>();
            Rect rect = arrow.rectTransform.rect;
            hitbox.isTrigger = true;
            hitbox.center = rect.center;
            hitbox.size = new Vector3(rect.width, rect.height, 40f);
            if (!arrow.TryGetComponent(out target))
                target = arrowObject.AddComponent<StagePageCylinder>();
        }
        target.Bind(controller, direction);
    }

    internal void SetPageControls(bool canGoPrevious, bool canGoNext)
    {
        if (previousArrow != null) previousArrow.gameObject.SetActive(canGoPrevious);
        if (nextArrow != null) nextArrow.gameObject.SetActive(canGoNext);
    }

    public void Show(Stage stage, StagePageIndex pages, DataManager data)
    {
        int stageId = stage.stageNum;

        if (stageTitle != null) stageTitle.text = $"스테이지 {stageId}";
        if (tipText != null) tipText.text = stage.tip ?? "";
        if (goalText != null) goalText.text = stage.goal.ToString();

        var ids = pages.GetIngredientIds(stageId);

        for (int i = 0; i < ingredients.Length; i++)
        {
            StageBoardItemUI slot = ingredients[i];

            if (slot == null) continue;
            if (i >= ids.Count || i >= 6) 
            { 
                slot.Clear(); 
                continue; 
            }

            data.GetIngredient().TryGet(ids[i], out Ingredient ingredient);
            slot.Bind(ingredient?.name ?? $"재료 #{ids[i]}", ingredient?.tag, ingredient?.IconName);
        }
        // 아직 이벤트 필드 없음
        foreach (StageBoardItemUI slot in events)
            if (slot != null) slot.Clear();
    }

    public void Clear(string status)
    {
        if (stageTitle != null) stageTitle.text = "스테이지";
        if (tipText != null) tipText.text = "";
        if (goalText != null) goalText.text = "";

        foreach (StageBoardItemUI slot in ingredients) 
            if (slot != null) slot.Clear();
        foreach (StageBoardItemUI slot in events) 
            if (slot != null) slot.Clear();

        SetStatus(status);
    }

    public void SetStatus(string status)
    {
        if (statusText != null) statusText.text = status ?? "";
    }
}
