using UnityEngine;

public class IngredientTraitOnion : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    private void SpawnTearArea(Vector3 position)
    {
        Debug.Log($"SpawnTearArea - Position: {position}");
        // 积己 夸没 菩哦 傈价
    }
}
