using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class IngredientTraitArea : MonoBehaviour
{
    public event Action<PlayerBrain> PlayerEntered;
    public event Action<PlayerBrain> PlayerStayed;
    public event Action<PlayerBrain> PlayerExited;

    [SerializeField] private LayerMask playerLayer;

    [Header("Movement Effect")]
    [Tooltip("이 영역에 들어온 플레이어에게 적용할 이동 효과. |" +
             "눈물: ChangeFriction만 켜고 FrictionMultiplier(0.008 ~ 0.015). |" +
             "꿀, 달걀: 둘 다 켜고 SpeedMultiplier를 낮게(0.3 ~ 0.5). | " +
             "Duration이 남아있어도 영역을 벗어나면 즉시 원복된다.")]
    [SerializeField] private PlayerEffectController.MovementEffectData movementEffect;

    // 랙돌 손/발처럼 한 플레이어에 콜라이더가 여러 개 붙어있는 경우,
    // 콜라이더 하나가 먼저 빠져나가도 다른 콜라이더가 아직 영역 안에 있으면
    // 효과가 꺼지면 안 되므로 플레이어별로 몇 개가 겹쳐있는지 센다.
    private readonly Dictionary<PlayerBrain, int> overlapCounts = new();

    private void Reset()
    {
        if (TryGetComponent(out Collider col))
        {
            col.isTrigger = true;
        }
    }

    private bool IsPlayer(Collider other, out PlayerBrain player)
    {
        player = null;

        if (((1 << other.gameObject.layer) & playerLayer) == 0)
            return false;

        // 랙돌 손/발 등은 PlayerBrain이 붙어있는 루트가 아니라
        // rig 하위 자식 오브젝트의 콜라이더이므로, 같은 오브젝트에서만 찾는
        // TryGetComponent 대신 부모 계층까지 탐색하는 GetComponentInParent를 사용한다.
        player = other.GetComponentInParent<PlayerBrain>();
        return player != null;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other, out PlayerBrain player))
            return;

        int count = overlapCounts.GetValueOrDefault(player, 0) + 1;
        overlapCounts[player] = count;

        // 이미 다른 콜라이더(반대쪽 발 등)로 겹쳐 있던 상태라면 중복 진입이므로 스킵.
        if (count > 1)
            return;

        PlayerEntered?.Invoke(player);

        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId))
            return;

        if (player.TryGetComponent(out PlayerEffectController effectController))
        {
            effectController.ApplyMovementEffect(movementEffect);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsPlayer(other, out PlayerBrain player))
            return;

        PlayerStayed?.Invoke(player);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other, out PlayerBrain player))
            return;

        int count = overlapCounts.GetValueOrDefault(player, 0) - 1;

        if (count > 0)
        {
            overlapCounts[player] = count;
            // 아직 다른 콜라이더가 영역 안에 남아있으므로 효과를 끄지 않는다.
            return;
        }

        overlapCounts.Remove(player);

        PlayerExited?.Invoke(player);

        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId))
            return;

        if (player.TryGetComponent(out PlayerEffectController effectController))
        {
            effectController.EndMovementEffect();
        }
    }
}