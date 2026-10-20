using Unity.Entities;
using Unity.Mathematics;

// Historical request Entity, used only by the isolated legacy comparison backends.
public struct IngredientSpawnRequest : IComponentData
{
    public int IngredientID;   // 재료 고유 ID (예: 10100)
    public long NetworkID;     // 서버가 부여한 고유 번호 (로컬은 0)
    public float3 Position;    // 생성 위치
    public quaternion Rotation; // 생성 회전 (쿼터니언)
    public int ConveyId;
}

