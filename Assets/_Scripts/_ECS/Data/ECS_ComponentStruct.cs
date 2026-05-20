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
public enum ThrowingType { parabola, javelin, shuriken, none }

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
    public FixedString32Bytes Tag;
}
public struct IngredientSpawnRequest : IComponentData
{
    public int IngredientID;   // 재료 고유 ID (예: 10100)
    public long NetworkID;     // 서버가 부여한 고유 번호 (로컬은 0)
    public float3 Position;    // 생성 위치
    public quaternion Rotation; // 생성 회전 (쿼터니언)
}

