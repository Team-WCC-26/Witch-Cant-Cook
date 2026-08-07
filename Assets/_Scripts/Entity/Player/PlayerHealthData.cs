using System;
using UnityEngine;

public sealed class PlayerHealthData
{
    public float CurHealth { get; private set; }
    public float MaxHealth { get; }
    public bool IsRagdoll => CurHealth <= 0f;
    public float NormalizedHealth => MaxHealth <= 0f ? 0f : CurHealth / MaxHealth;

    public event Action<PlayerHealthData> HealthChanged;

    private readonly float damageCooldown;
    private float nextDamageTime;

    public PlayerHealthData(float maxHealth, float damageCooldown)
    {
        MaxHealth = Math.Max(0f, maxHealth);
        this.damageCooldown = Math.Max(0f, damageCooldown);
        Reset();
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsRagdoll || Time.time < nextDamageTime) return;

        SetHealth(CurHealth - amount);
        nextDamageTime = Time.time + damageCooldown;
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || IsRagdoll) return;
        SetHealth(CurHealth + amount);
    }

    public void Reset()
    {
        nextDamageTime = 0f;
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
