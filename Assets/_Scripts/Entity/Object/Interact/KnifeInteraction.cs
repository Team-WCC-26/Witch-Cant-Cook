using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class KnifeInteraction : MonoBehaviour, IHeldPrimaryAction, IEquipment
{
    private CatchableObj catchable;
    private bool thrownDamageArmed;
    private string throwerPlayerId;

    private void OnEnable()
    {
        catchable = GetComponent<CatchableObj>();
        catchable.Thrown += OnThrown;
        catchable.OnPicked += DisarmThrownDamage;
        catchable.OnDropped += DisarmThrownDamage;
        catchable.OnDespawned += DisarmThrownDamage;
    }

    private void OnDisable()
    {
        if (catchable != null)
        {
            catchable.Thrown -= OnThrown;
            catchable.OnPicked -= DisarmThrownDamage;
            catchable.OnDropped -= DisarmThrownDamage;
            catchable.OnDespawned -= DisarmThrownDamage;
        }
        DisarmThrownDamage();
    }

    public bool TryUsePrimary(PlayerInteract interact)
    {
        if (interact == null) return false;
        if (!interact.TryUseEquipment()) return true;
        interact.QueueKnifeImpact();
        return true;
    }

    private void OnThrown(Vector3 velocity)
    {
        DisarmThrownDamage();
        if (velocity.sqrMagnitude <= 0f || string.IsNullOrEmpty(catchable.LastHolderPlayerId)) return;
        throwerPlayerId = catchable.LastHolderPlayerId;
        thrownDamageArmed = true;
    }

    private void DisarmThrownDamage()
    {
        thrownDamageArmed = false;
        throwerPlayerId = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleThrownCollision(collision.collider);
    }

    private void HandleThrownCollision(Collider other)
    {
        if (!thrownDamageArmed || catchable.IsHold || catchable.ParentEntityId > 0 ||
            other == null || other.isTrigger) return;

        PlayerBrain target = other.GetComponentInParent<PlayerBrain>();
        // Leaving the hand can overlap the thrower's body without consuming the throw.
        if (target != null && target.PlayerId == throwerPlayerId) return;

        string attackerId = throwerPlayerId;
        // One impact per throw; ground/wall contacts also disarm a resting knife.
        DisarmThrownDamage();
        if (target == null || PlayerSpawnManager.Instance == null ||
            !PlayerSpawnManager.Instance.TryGetPlayer(attackerId, out PlayerBrain attacker)) return;

        PlayerInteract.ApplyPlayerHit(attacker, target, SoundId.KnifeHit);
    }
}
