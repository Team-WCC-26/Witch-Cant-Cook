using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

[RequireComponent(typeof(CatchableObj))]
public class IngredientTraitExplode : IngredientTrait
{
    [Header("Explosion")]
    [SerializeField] private GameObject explosionVFXPrefab;
    [SerializeField, Min(0f)] private float blastRadius = 10f;
    [SerializeField, Min(0f)] private float blastForce = 12f;
    [SerializeField, Min(0f)] private float upwardModifier = 2f;
    [SerializeField] private LayerMask blastMask = ~0;

    [Header("Player Knockback")]
    [SerializeField, Min(0f)] private float playerForceMultiplier = 2.2f;
    [SerializeField, Min(0f)] private float playerSpinImpulse = 0.8f;
    [SerializeField, Min(0f)] private float playerKnockbackDuration = 0.8f;

    [Header("Replacement Ingredient")]
    [SerializeField] private Define.eIngredient replacementIngredient = Define.eIngredient.Popcorn;
    [SerializeField, Min(0f)] private float replacementHeightOffset = 0.15f;

    private CatchableObj source;
    private bool exploded;
    private readonly HashSet<Rigidbody> affectedBodies = new();

    private void Awake()
    {
        source = GetComponent<CatchableObj>();
    }

    protected override void StartTrait()
    {
        exploded = false;
        affectedBodies.Clear();
    }

    public void Explode()
    {
        if (exploded) return;
        exploded = true;

        Vector3 origin = transform.position;
        if (explosionVFXPrefab != null)
            Instantiate(explosionVFXPrefab, origin, Quaternion.identity);

        ApplyBlast(origin);
        SpawnReplacement(origin);
    }

    private void ApplyBlast(Vector3 origin)
    {
        affectedBodies.Clear();
        Collider[] hits = Physics.OverlapSphere(
            origin, blastRadius, blastMask, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;

            PlayerBrain player = hit.GetComponentInParent<PlayerBrain>();
            if (player != null)
            {
                Rigidbody playerBody = player.Rb;
                if (playerBody == null || playerBody.isKinematic || !affectedBodies.Add(playerBody))
                    continue;

                if (player.Interact.HeldObj != null && player.Interact.HeldObj != source)
                    player.Interact.ForceDropHeld();

                // 이동 코드가 다음 FixedUpdate에서 폭발 속도/회전을 덮어쓰지 않도록 한다.
                player.ActionController.Movement.BeginForcedMovement(playerKnockbackDuration);
                ApplyKnockback(playerBody, origin, playerForceMultiplier);
                float spinDirection = Random.value < 0.5f ? -1f : 1f;
                playerBody.AddTorque(Vector3.up * (playerSpinImpulse * spinDirection), ForceMode.Impulse);
                continue;
            }

            CatchableObj catchable = hit.GetComponentInParent<CatchableObj>();
            if (catchable != null && (catchable == source || catchable.IsHold)) continue;

            Rigidbody rb = hit.attachedRigidbody;
            if (rb == null || rb.isKinematic || !affectedBodies.Add(rb)) continue;

            ApplyKnockback(rb, origin, 1f);
        }
    }

    private void ApplyKnockback(Rigidbody rb, Vector3 origin, float multiplier)
    {
        Vector3 direction = rb.worldCenterOfMass - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            Vector2 randomDirection = Random.insideUnitCircle;
            direction = new Vector3(randomDirection.x, 0f, randomDirection.y);
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
        }

        Vector3 impulse = direction.normalized * (blastForce * multiplier) +
                          Vector3.up * upwardModifier;
        rb.AddForce(impulse, ForceMode.Impulse);
    }

    private void SpawnReplacement(Vector3 origin)
    {
        IngredientNetworkBridge bridge = IngredientNetworkBridge.Instance;
        if (bridge == null)
        {
            Debug.LogError("[Explosion] IngredientNetworkBridge is required to spawn the replacement ingredient.");
            return;
        }

        // 일반 재료 생성 경로를 사용해 데이터, NetworkID, 잡기/조리/ECS 등록을 유지한다.
        Vector3 position = origin + Vector3.up * replacementHeightOffset;
        bridge.SendSpawnPacketToServer((int)replacementIngredient, (float3)position);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, blastRadius);
    }
#endif
}
