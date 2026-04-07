using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

// 모든 재료 엔티티를 찾기 위한 순수 마커 태그
public struct IngredientTag : IComponentData { }

// 정적 정보 (변경되지 않는 고유 값들)
public struct IngredientInfo : IComponentData
{
    public int EntityID;      // 시트의 id
    public FixedString64Bytes Name;
}

// 실시간 체력 (변화가 잦은 데이터만 따로 분리)
public struct Health : IComponentData
{
    public int Current;
    public int Max;
}

// 물리 관련 데이터
public struct PhysicsWeight : IComponentData
{
    public float Value; // 시트의 weight
}

// 투척 형태를 정의 (Enum 활용)
public enum ThrowingType { Linear, Parabolic, None }

public struct ProjectileData : IComponentData
{
    public ThrowingType Type;
    public int BaseDamage; // 시트의 damage
}

// 테이블 대응되는 태그들 개별로 제작
//public struct FireTag : IComponentData { }
//public struct SpicyTag : IComponentData { }
//public struct VegetableTag : IComponentData { }