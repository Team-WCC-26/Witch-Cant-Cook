using System;
using TMPro;
using UnityEngine;

public class StageSelectionWorldUI : MonoBehaviour
{
    [SerializeField] private TMP_Text stageTitle;
    [SerializeField] private TMP_Text tipText;
    [SerializeField] private TMP_Text goalText;
    [SerializeField] private TMP_Text statusText;
    // Retain the serialized links so existing scene arrows can be hidden without rebuilding their UI.
    [SerializeField, HideInInspector] private TMP_Text previousArrow;
    [SerializeField, HideInInspector] private TMP_Text nextArrow;

    [Tooltip("왼쪽 열 위에서 아래 3개, 오른쪽 열 위에서 아래 3개 순서")]
    [SerializeField] private StageBoardItemUI[] ingredients = Array.Empty<StageBoardItemUI>();
    [SerializeField] private StageBoardItemUI[] events = Array.Empty<StageBoardItemUI>();

    public void Configure(TMP_Text title, TMP_Text tip, TMP_Text goal, TMP_Text status,
        StageBoardItemUI[] ingredientSlots, StageBoardItemUI[] eventSlots)
    {
        stageTitle = title; tipText = tip; goalText = goal; statusText = status;
        ingredients = ingredientSlots; events = eventSlots;
        HidePageControls();
    }

    private void OnEnable() => HidePageControls();

    private void HidePageControls()
    {
        if (previousArrow != null) previousArrow.gameObject.SetActive(false);
        if (nextArrow != null) nextArrow.gameObject.SetActive(false);
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
        HidePageControls();
        if (statusText != null) statusText.text = status ?? "";
    }
}
