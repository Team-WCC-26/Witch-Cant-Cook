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

    [Tooltip("왼쪽 열 위에서 아래 3개, 오른쪽 열 위에서 아래 3개 순서")]
    [SerializeField] private StageBoardItemUI[] ingredients = Array.Empty<StageBoardItemUI>();
    [SerializeField] private StageBoardItemUI[] events = Array.Empty<StageBoardItemUI>();

    public void Configure(TMP_Text title, TMP_Text tip, TMP_Text goal, TMP_Text status,
        TMP_Text previous, TMP_Text next, StageBoardItemUI[] ingredientSlots, StageBoardItemUI[] eventSlots)
    {
        stageTitle = title; tipText = tip; goalText = goal; statusText = status;
        previousArrow = previous; nextArrow = next; ingredients = ingredientSlots; events = eventSlots;
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

        SetNavigation(false, false, status);
    }

    public void SetNavigation(bool previousAvailable, bool nextAvailable, string status)
    {
        if (previousArrow != null) previousArrow.color = previousAvailable ? Color.black : Color.gray;
        if (nextArrow != null) nextArrow.color = nextAvailable ? Color.black : Color.gray;
        if (statusText != null) statusText.text = status ?? "";
    }
}
