using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider), typeof(Rigidbody))]
public class IngredientTraitArea : MonoBehaviour
{
    public event Action<PlayerBrain> PlayerEntered;
    public event Action<PlayerBrain> PlayerStayed;
    public event Action<PlayerBrain> PlayerExited;

    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask groundLayer;
    [Header("Landing")]
    [Tooltip("Non-trigger collider on a child object without its own Rigidbody.")]
    [SerializeField] private Collider landingCollider;
    [SerializeField, Range(0.01f, 1f)] private float minGroundNormalY = 0.5f;

    [Header("Movement Effect")]
    [SerializeField] private PlayerEffectController.MovementEffectData movementEffect;

    private Collider col;
    private Rigidbody rb;
    private bool landed;
    private readonly Dictionary<PlayerBrain, int> overlapCounts = new();
    private readonly Dictionary<Collider, PlayerBrain> overlappingColliders = new();

    private void Awake()
    {
        col = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();
        col.isTrigger = true;
    }

    private void OnEnable()
    {
        landed = false;
        if (landingCollider == null || landingCollider == col ||
            !landingCollider.transform.IsChildOf(transform) || landingCollider.attachedRigidbody != rb)
        {
            Debug.LogError("IngredientTraitArea requires a child Landing Collider sharing the root Rigidbody.", this);
            rb.isKinematic = true;
            rb.useGravity = false;
            enabled = false;
            return;
        }

        col.isTrigger = true;
        landingCollider.isTrigger = false;
        landingCollider.enabled = true;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.constraints |= RigidbodyConstraints.FreezeRotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.WakeUp();
    }

    private void Reset()
    {
        if (TryGetComponent(out Collider c)) c.isTrigger = true;
        if (TryGetComponent(out Rigidbody r))
        {
            r.isKinematic = false;
            r.useGravity = true;
            r.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    private void OnCollisionEnter(Collision collision) => TryLand(collision);
    private void OnCollisionStay(Collision collision) => TryLand(collision);

    private void TryLand(Collision collision)
    {
        if (landed) return;
        if (((1 << collision.collider.gameObject.layer) & groundLayer) == 0) return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.thisCollider != landingCollider || contact.normal.y < minGroundNormalY) continue;

            landed = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
            landingCollider.enabled = false;
            ScanPlayersOnLanding();
            return;
        }
    }

    private void ScanPlayersOnLanding()
    {
        // Bounds are world-aligned. Narrow the broad-phase query to the actual trigger shape.
        Bounds bounds = col.bounds;
        Collider[] hits = Physics.OverlapBox(bounds.center, bounds.extents,
            Quaternion.identity, playerLayer, QueryTriggerInteraction.Collide);
        foreach (Collider hit in hits)
        {
            if (!Physics.ComputePenetration(col, col.transform.position, col.transform.rotation,
                hit, hit.transform.position, hit.transform.rotation, out _, out _)) continue;
            RegisterPlayer(hit);
        }
    }

    private bool IsPlayer(Collider other, out PlayerBrain player)
    {
        player = null;
        if (((1 << other.gameObject.layer) & playerLayer) == 0) return false;
        player = other.GetComponentInParent<PlayerBrain>();
        return player != null;
    }

    private void RegisterPlayer(Collider other)
    {
        if (!landed || overlappingColliders.ContainsKey(other)) return;
        if (!IsPlayer(other, out PlayerBrain player)) return;
        overlappingColliders.Add(other, player);
        int count = overlapCounts.GetValueOrDefault(player, 0) + 1;
        overlapCounts[player] = count;
        if (count > 1) return;

        PlayerEntered?.Invoke(player);
        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        if (player.TryGetComponent(out PlayerEffectController effectController))
            effectController.ApplyMovementEffect(movementEffect);
    }

    private void OnTriggerEnter(Collider other) => RegisterPlayer(other);

    private void OnTriggerStay(Collider other)
    {
        // Also handles players already overlapping while the area was falling.
        RegisterPlayer(other);
        if (overlappingColliders.TryGetValue(other, out PlayerBrain player))
            PlayerStayed?.Invoke(player);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!overlappingColliders.TryGetValue(other, out PlayerBrain player)) return;
        overlappingColliders.Remove(other);
        int count = overlapCounts[player] - 1;
        if (count > 0)
        {
            overlapCounts[player] = count;
            return;
        }
        overlapCounts.Remove(player);
        if (player == null) return;
        PlayerExited?.Invoke(player);
        EndMovementEffect(player);
    }

    private void EndMovementEffect(PlayerBrain player)
    {
        if (player == null || PlayerSpawnManager.Instance == null) return;
        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        if (player.TryGetComponent(out PlayerEffectController effectController))
            effectController.EndMovementEffect();
    }

    private void OnDisable()
    {
        foreach (PlayerBrain player in overlapCounts.Keys)
            EndMovementEffect(player);
        overlapCounts.Clear();
        overlappingColliders.Clear();
        landed = false;
    }
}
