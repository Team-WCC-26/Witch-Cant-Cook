using UnityEngine;

[RequireComponent(typeof(IngredientReaction), typeof(IngredientTraitBounce))]
public class IngredientMushroom : IngredientTrait
{
    // IngredientReaction stops bounce after preparation and restores raw bounce state for pooling.
}
