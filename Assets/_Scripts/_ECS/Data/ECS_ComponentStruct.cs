using Unity.Entities;

// Retained for the existing cooking-tool ECS path; ingredient spawning no longer creates Entities.
public struct Health : IComponentData
{
    public int Current;
    public int Max;
}
