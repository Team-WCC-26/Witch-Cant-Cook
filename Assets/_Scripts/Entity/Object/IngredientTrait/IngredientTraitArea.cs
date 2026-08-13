using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class IngredientTraitArea : MonoBehaviour
{
    [Serializable]
    public struct AreaEffectData
    {
        [Header("마찰 감소 효과 (Egg, Onion 등)")]
        public bool ChangeFriction;
        [Range(0f, 1f)]
        public float FrictionMultiplier;

        [Header("속도 증가 효과 (선택, 미끄러짐과 함께 사용 가능)")]
        public bool ChangeSpeed;
        [Min(0f)]
        public float SpeedMultiplier;

        [Header("정지 효과 (Honey 등, 최초 진입 시 1회만 적용)")]
        public bool StopOnEnter;
    }
    public event Action<PlayerBrain> PlayerEntered;
    public event Action<PlayerBrain> PlayerStayed;
    public event Action<PlayerBrain> PlayerExited;

    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private AreaEffectData effectData;

    // 영역이 파괴/비활성화될 때 안에 남아있는 플레이어의 효과를 해제하기 위한 추적용
    private readonly HashSet<PlayerBrain> playersInside = new();

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

        player = other.GetComponentInParent<PlayerBrain>();
        return player != null;
    }
    private bool TryGetEffectController(PlayerBrain player, out PlayerEffectController controller)
    {
        controller = player != null ? player.GetComponent<PlayerEffectController>() : null;
        return controller != null;
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[Area] Trigger Enter: {other.name}");
        if (!IsPlayer(other, out PlayerBrain player))
        {
            Debug.Log($"[Area] Not recognized as player. layer={other.gameObject.layer}");
            return;
        }
        Debug.Log($"[Area] Player entered: {player.PlayerId}, ChangeFriction={effectData.ChangeFriction}, value={effectData.FrictionMultiplier}");

        playersInside.Add(player);

        if (TryGetEffectController(player, out PlayerEffectController controller))
        {
            if (effectData.ChangeFriction)
                controller.EnterFrictionArea(this, effectData.FrictionMultiplier);

            if (effectData.ChangeSpeed) // 추가
                controller.EnterSpeedArea(this, effectData.SpeedMultiplier);

            if (effectData.StopOnEnter)
                controller.ApplyStopOnEnter();
        }

        PlayerEntered?.Invoke(player);
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

        playersInside.Remove(player);

        if (TryGetEffectController(player, out PlayerEffectController controller))
        {
            if (effectData.ChangeFriction)
                controller.ExitFrictionArea(this);

            if (effectData.ChangeSpeed) // 추가
                controller.ExitSpeedArea(this);
        }

        PlayerExited?.Invoke(player);
    }
    // 아이템 지속시간 만료 등으로 영역 오브젝트가 먼저 사라지는 경우,
    // 안에 있던 플레이어의 마찰 효과가 영구히 남는 것을 방지
    private void OnDisable()
    {
        if (playersInside.Count == 0)
            return;

        foreach (PlayerBrain player in playersInside)
        {
            if (!TryGetEffectController(player, out PlayerEffectController controller))
                continue;

            if (effectData.ChangeFriction)
                controller.ExitFrictionArea(this);

            if (effectData.ChangeSpeed) 
                controller.ExitSpeedArea(this);
        }
        playersInside.Clear();
    }
}