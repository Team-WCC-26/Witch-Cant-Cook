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

    [Header("Movement Effect")]
    [SerializeField] private PlayerEffectController.MovementEffectData movementEffect;

    private Collider col;
    private Rigidbody rb;
    private bool landed = false;

    private readonly Dictionary<PlayerBrain, int> overlapCounts = new();

    private void Awake()
    {
        col = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();

        col.isTrigger = false;
    }

    private void Reset()
    {
        if (TryGetComponent(out Collider c))
            c.isTrigger = false;

        if (TryGetComponent(out Rigidbody r))
        {
            r.isKinematic = false;
            r.useGravity = true;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (landed) return;
        if (((1 << collision.gameObject.layer) & groundLayer) == 0) return;

        landed = true;
        rb.isKinematic = true;
        rb.useGravity = false;
        col.isTrigger = true;

        // isTrigger로 전환되는 순간 이미 영역과 겹쳐있던 플레이어는
        // OnTriggerEnter가 발생하지 않으므로 직접 스캔해서 효과 적용
        ScanPlayersOnLanding();
    }

    private void ScanPlayersOnLanding()
    {
        Bounds bounds = col.bounds;
        // col.bounds는 isTrigger 전환 직후라 이미 트리거 영역 크기와 동일
        Collider[] hits = Physics.OverlapBox(
            bounds.center,
            bounds.extents,
            transform.rotation,
            playerLayer,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider hit in hits)
        {
            if (!IsPlayer(hit, out PlayerBrain player)) continue;

            int count = overlapCounts.GetValueOrDefault(player, 0) + 1;
            overlapCounts[player] = count;

            // 이미 다른 파츠로 카운트된 플레이어는 이벤트/효과 중복 적용 방지
            if (count > 1) continue;

            PlayerEntered?.Invoke(player);

            if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) continue;

            if (player.TryGetComponent(out PlayerEffectController effectController))
                effectController.ApplyMovementEffect(movementEffect);
        }
    }
    private bool IsPlayer(Collider other, out PlayerBrain player)
    {
        player = null;
        if (((1 << other.gameObject.layer) & playerLayer) == 0)
            return false;

        player = other.GetComponentInParent<PlayerBrain>();
        return player != null;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!landed) return;
        if (!IsPlayer(other, out PlayerBrain player)) return;

        int count = overlapCounts.GetValueOrDefault(player, 0) + 1;
        overlapCounts[player] = count;

        if (count > 1) return;

        PlayerEntered?.Invoke(player);

        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;

        if (player.TryGetComponent(out PlayerEffectController effectController))
            effectController.ApplyMovementEffect(movementEffect);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!landed) return;
        if (!IsPlayer(other, out PlayerBrain player)) return;

        PlayerStayed?.Invoke(player);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!landed) return;
        if (!IsPlayer(other, out PlayerBrain player)) return;

        int count = overlapCounts.GetValueOrDefault(player, 0) - 1;

        if (count > 0)
        {
            overlapCounts[player] = count;
            return;
        }

        overlapCounts.Remove(player);
        PlayerExited?.Invoke(player);

        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;

        if (player.TryGetComponent(out PlayerEffectController effectController))
            effectController.EndMovementEffect();
    }

    private void OnDisable()
    {
        if (overlapCounts.Count == 0) return;

        foreach (PlayerBrain player in overlapCounts.Keys)
        {
            if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) continue;

            if (player.TryGetComponent(out PlayerEffectController effectController))
                effectController.EndMovementEffect();
        }
        overlapCounts.Clear();
    }
}