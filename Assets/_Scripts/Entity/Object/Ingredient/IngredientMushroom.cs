using UnityEngine;

[RequireComponent(typeof(IngredientReaction), typeof(IngredientTraitBounce))]
public class IngredientMushroom : IngredientTrait
{
    // IngredientReaction manages cut suppression and restores the original bounce state for pooling.
}
