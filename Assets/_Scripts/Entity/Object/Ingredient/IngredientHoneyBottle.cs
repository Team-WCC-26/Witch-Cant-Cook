using System.Net.NetworkInformation;
using UnityEngine;

public class IngredientHoneyBottle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Define.eIngredient eArea = Define.eIngredient.HoneyLiquid;

    private IngredientTraitFragile fragileTrait;
    private IngredientTraitAreaCreator areaCreator;

    private void Awake()
    {
        fragileTrait = GetComponent<IngredientTraitFragile>();
        areaCreator = GetComponent<IngredientTraitAreaCreator>();

        fragileTrait.OnBroken += OnBroken;
    }

    private void OnDestroy()
    {
        fragileTrait.OnBroken -= OnBroken;
    }

    private void OnBroken()
    {
        if (areaCreator != null)
        {
            areaCreator.CreateArea(eArea);
        }
    }
}
