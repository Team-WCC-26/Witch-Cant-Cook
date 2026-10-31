using System;
using UnityEngine;

public sealed class PlayerHealthData
{
    public float CurHealth { get; private set; }
    public float MaxHealth { get; }
    public bool IsDmgBlocked { get; set; }
    public bool IsRagdoll { get; private set; }
    public bool IsRecoveringFromDeath { get; private set; }
    public float NormalizedHealth => MaxHealth <= 0f ? 0f : CurHealth / MaxHealth;

    public event Action<PlayerHealthData> HealthChanged;
    public event Action<int> DamageRequested;
    public event Action DeathConfirmed;

    private readonly bool isLocalPlayer;
    private readonly float damageCooldown;
    private float nextDamageTime;

    public PlayerHealthData(float maxHealth, float damageCooldown, bool isLocalPlayer)
    {
        MaxHealth = Math.Max(0f, maxHealth);
        this.isLocalPlayer = isLocalPlayer;
        this.damageCooldown = Math.Max(0f, damageCooldown);
        CurHealth = MaxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (!isLocalPlayer || float.IsNaN(amount) || float.IsInfinity(amount) ||
            amount <= 0f || IsDmgBlocked || IsRagdoll || Time.time < nextDamageTime) return;

        int dmg = Mathf.RoundToInt(Mathf.Min(amount, MaxHealth));
        if (dmg <= 0) return;
        nextDamageTime = Time.time + damageCooldown;
        DamageRequested?.Invoke(dmg);
    }

    public void InstantDeath()
    {
        if (!isLocalPlayer || IsDmgBlocked || IsRagdoll) return;
        DamageRequested?.Invoke(Mathf.CeilToInt(MaxHealth));
    }

    public void ApplyServerHealth(int health)
    {
        SetHealth(health);
    }

    public void RestoreFromServerRespawn()
    {
        nextDamageTime = 0f;
        IsRecoveringFromDeath = IsRagdoll;
        SetHealth(MaxHealth);
        DeathConfirmed?.Invoke();
    }

    public void SetRagdollActive(bool active)
    {
        IsRagdoll = active;
        if (!active) IsRecoveringFromDeath = false;
    }

    private void SetHealth(float health)
    {
        float nextHealth = Math.Clamp(health, 0f, MaxHealth);
        if (CurHealth == nextHealth) return;

        CurHealth = nextHealth;
        HealthChanged?.Invoke(this);
    }

#if UNITY_EDITOR
    #region Editor
    public void DebugDepleteHealth()
    {
        InstantDeath();
    }
    #endregion
#endif
}
