using UnityEngine;

[RequireComponent(typeof(IngredientReaction), typeof(IngredientTraitBounce))]
public class IngredientMushroom : IngredientTrait
{
    private IngredientReaction reaction;
    private IngredientTraitBounce bounce;
    private bool originalBounceEnabled;

    private void Awake()
    {
        reaction = GetComponent<IngredientReaction>();
        bounce = GetComponent<IngredientTraitBounce>();
        originalBounceEnabled = bounce.enabled;
    }

    protected override void StartTrait()
    {
        reaction.OnActionCompleted += OnActionCompleted;
        if (reaction.IsActionCompleted(IngredientAction.Cut))
            bounce.enabled = false;
    }

    protected override void StopTrait()
    {
        reaction.OnActionCompleted -= OnActionCompleted;
        bounce.enabled = originalBounceEnabled;
    }

    private void OnActionCompleted(IngredientAction action)
    {
        if ((action & IngredientAction.Cut) != 0)
            bounce.enabled = false;
    }
}
