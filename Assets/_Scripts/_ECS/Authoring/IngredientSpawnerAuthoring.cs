using System.Collections.Generic;
using Unity.Collections; // FixedString 사용을 위해 필요
using Unity.Entities;
using UnityEngine;

public class IngredientSpawnerAuthoring : MonoBehaviour
{
    [System.Serializable]
    public struct IngredientAddressMap
    {
        public int ingredientID;
        public string addressableKey; // 어드레서블에 등록된 이름 (예: "Apple", "Carrot")
    }

    public List<IngredientAddressMap> library;

    public class Baker : Baker<IngredientSpawnerAuthoring>
    {
        public override void Bake(IngredientSpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var buffer = AddBuffer<IngredientAddressBuffer>(entity);

            foreach (var item in authoring.library)
            {
                buffer.Add(new IngredientAddressBuffer
                {
                    IngredientID = item.ingredientID,
                    // ECS 내부에서는 string 대신 FixedString을 사용
                    AddressKey = new FixedString64Bytes(item.addressableKey)
                });
            }
        }
    }
}

// ECS 데이터 구조체
public struct IngredientAddressBuffer : IBufferElementData
{
    public int IngredientID;
    public FixedString64Bytes AddressKey;
}