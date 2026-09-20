using Protocol;
using Server;
using UnityEngine;

public class IngredientEgg : IngredientTrait
{
    [Header("References")]
    [SerializeField] private Define.eIngredient eArea = Define.eIngredient.EggInside;

    private IngredientTraitFragile fragileTrait;
    private IngredientTraitAreaCreator areaCreator;

    private CatchableObj catchable; 


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

    private void OnBroken()
    {
        Debug.Log($"[Egg] OnBroken called. netID = {catchable.NetworkId}, instance={GetInstanceID()}, frame={Time.frameCount}");

        if (areaCreator != null)
        {
            areaCreator.CreateArea(eArea);
            PushIngredientToPool(catchable);

        }

    }
}
