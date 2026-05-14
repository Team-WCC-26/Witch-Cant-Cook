using Unity.Entities;
using Unity.Mathematics;

public struct NetworkID : IComponentData
{
    public long Value;
}

public struct NetworkRemoteSync : IComponentData
{
    public float3 TargetPosition;
    public quaternion TargetRotation;
    public float InterpolationSpeed;
}

// 재료 상태 플래그
public struct IngredientStateFlag : IComponentData
{
    public byte Value;
    public bool HasFlag(byte flag) => (Value & flag) != 0;
}