using Protocol;
using Server;
using System.Net.NetworkInformation;
using UnityEngine;

public class IngredientHoneyBottle : IngredientTrait
{
    [Header("References")]
    [SerializeField] private Define.eIngredient eArea = Define.eIngredient.HoneyLiquid;

    private IngredientTraitFragile fragileTrait;
    private IngredientTraitAreaCreator areaCreator;

    private CatchableObj catchable;
    private bool areaCreated = false;


    private void Awake()
    {
        fragileTrait = GetComponent<IngredientTraitFragile>();
        areaCreator = GetComponent<IngredientTraitAreaCreator>();
        catchable = GetComponent<CatchableObj>();

        if (fragileTrait != null)
            fragileTrait.OnBroken += OnBroken;
    }

    private void OnDestroy()
    {
        if (fragileTrait != null)
            fragileTrait.OnBroken -= OnBroken;
    }

    protected override void StartTrait()
    {
        areaCreated = false;
    }

    private void OnBroken(Vector3 point, Vector3 normal)
    {
        if (areaCreated) return;
        areaCreated = true;

        if (areaCreator != null)
        {
            areaCreator.CreateArea(eArea, point, normal);
            PushIngredientToPool(catchable);
        }
    }
}
