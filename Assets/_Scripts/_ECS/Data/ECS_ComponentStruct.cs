using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

// 모든 재료 엔티티를 찾기 위한 순수 마커 태그
public struct IngredientTag : IComponentData { }

// 정적 정보 (변경되지 않는 고유 값들)
public struct IngredientInfo : IComponentData
{
    public int ID;      // 시트의 id
    public FixedString64Bytes Name;
}

// 실시간 체력
public struct Health : IComponentData
{
    public int Current;
    public int Max;
}

/// <summary>
/// 투척 형태
/// </summary>
public enum ThrowingType { Linear, Parabolic, None }

// 물리 특성
public struct IngredientPhysics : IComponentData
{
    public float Weight;
    public ThrowingType Throwing;
}

public struct IngredientCombat : IComponentData
{
    public int Damage;
    // 태그의 경우 enum이나 정수형 ID로 관리하는 것이 최적화에 좋습니다.
    public int TagID;
}
public struct IngredientSpawnRequest : IComponentData
{
    public int IngredientID;      // 어떤 재료를 만들 것인가?
    public Unity.Mathematics.float3 Position; // 어디에 만들 것인가?
}
