using System;

public sealed class PlayerHealthData
{
    public float CurHealth { get; private set; }
    public float MaxHealth { get; }
    public bool IsRagdoll => CurHealth <= 0f;
    public float NormalizedHealth => MaxHealth <= 0f ? 0f : CurHealth / MaxHealth;

    public event Action<PlayerHealthData> HealthChanged;

    public PlayerHealthData(float maxHealth)
    {
        MaxHealth = Math.Max(0f, maxHealth);
        Reset();
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsRagdoll) return;
        SetHealth(CurHealth - amount);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || IsRagdoll) return;
        SetHealth(CurHealth + amount);
    }

    public void Reset()
    {
        SetHealth(MaxHealth);
    }

    private void SetHealth(float health)
    {
        float nextHealth = Math.Clamp(health, 0f, MaxHealth);
        if (CurHealth == nextHealth) return;

        CurHealth = nextHealth;
        HealthChanged?.Invoke(this);
    }
}