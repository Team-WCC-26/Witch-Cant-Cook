using System.Collections.Generic;
using UnityEngine;

public class PotVisualController : MonoBehaviour
{
    [SerializeField] private PotItemContainer soupContainer;
    [SerializeField] private PotItemContainer stewContainer;
    [SerializeField] private PotVisualData[] potVisuals;

    private Dictionary<int, PotVisualData> soupVisualDict = new();
    private Dictionary<int, PotVisualData> stewVisualDict = new();

    private void Awake()
    {
        foreach (var visual in potVisuals)
        {
            if (visual == null) continue;

            switch (visual.PotType)
            {
                case PotType.Soup:
                    soupVisualDict[visual.IngredientId] = visual;
                    break;

                case PotType.Stew:
                    stewVisualDict[visual.IngredientId] = visual;
                    break;
            }
        }

        HideAll();
    }

    public void UpdateVisual(int ingredientId, bool isDone = false)
    {
        HideAll();

        Dictionary<int, PotVisualData> targetDict = isDone ? stewVisualDict : soupVisualDict;

        if (!targetDict.TryGetValue(ingredientId, out PotVisualData visualData))
        {
            Debug.LogWarning($"Pot visual data not found. IngredientId: {ingredientId}, IsDone: {isDone}");
            return;
        }

        if (isDone) SetStewVisual(visualData);
        else SetSoupVisual(visualData);
    }

    private void HideAll()
    {
        soupContainer.gameObject.SetActive(false);
        stewContainer.gameObject.SetActive(false);
    }   

    private void SetSoupVisual(PotVisualData visualData)
    {
        soupContainer.PotMeshFilter.sharedMesh = visualData.PotMesh;
        soupContainer.PotMeshRenderer.sharedMaterials = visualData.PotMaterials;
        soupContainer.transform.localScale = visualData.Scale;
        soupContainer.gameObject.SetActive(true);
    }

    private void SetStewVisual(PotVisualData visualData)
    {
        stewContainer.PotMeshFilter.sharedMesh = visualData.PotMesh;
        stewContainer.PotMeshRenderer.sharedMaterials = visualData.PotMaterials;
        stewContainer.transform.localScale = visualData.Scale;
        stewContainer.gameObject.SetActive(true);
    }
}