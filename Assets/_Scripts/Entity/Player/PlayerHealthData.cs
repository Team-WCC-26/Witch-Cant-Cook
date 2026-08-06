using System;

public sealed class PlayerHealthData
{
    public float CurrentHealth { get; private set; }
    public float MaxHealth { get; }
    public bool IsRagdoll => CurrentHealth <= 0f;
    public float NormalizedHealth => MaxHealth <= 0f ? 0f : CurrentHealth / MaxHealth;

    public event Action<PlayerHealthData> HealthChanged;

    public PlayerHealthData(float maxHealth)
    {
        MaxHealth = Math.Max(0f, maxHealth);
        CurrentHealth = MaxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsRagdoll)
        {
            return;
        }

        SetCurrentHealth(CurrentHealth - amount);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || IsRagdoll)
        {
            return;
        }

        SetCurrentHealth(CurrentHealth + amount);
    }

    public void Reset()
    {
        SetCurrentHealth(MaxHealth);
    }

    private void SetCurrentHealth(float health)
    {
        float nextHealth = Math.Max(0f, Math.Min(health, MaxHealth));
        if (CurrentHealth == nextHealth)
        {
            return;
        }

        CurrentHealth = nextHealth;
        HealthChanged?.Invoke(this);
    }
}
