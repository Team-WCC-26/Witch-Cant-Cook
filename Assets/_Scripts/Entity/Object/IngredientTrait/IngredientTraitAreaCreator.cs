using UnityEngine;
using Unity.Mathematics;

public class IngredientTraitAreaCreator : MonoBehaviour
{
    [SerializeField, Min(0f)] private float spawnHeightOffset = 0.15f;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.1f;

    public void CreateArea(Define.eIngredient ingredient)
    {
        Spawn(ingredient, transform.position + Vector3.up * spawnHeightOffset);
    }

    public void CreateArea(Define.eIngredient ingredient, Vector3 point, Vector3 normal)
    {
        Spawn(ingredient, point + normal * surfaceOffset + Vector3.up * spawnHeightOffset);
    }

    private void Spawn(Define.eIngredient ingredient, Vector3 position)
    {
        IngredientNetworkBridge.Instance.SendSpawnPacketToServer(
            (int)ingredient, new float3(position.x, position.y, position.z));
    }
}
