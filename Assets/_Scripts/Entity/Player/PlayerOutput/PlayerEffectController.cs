using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerEffectController : MonoBehaviour
{
    private PlayerBrain brain;
    private Coroutine movementCoroutine;

    // 타이머 기반 이동 효과가 사용하는 고정 키 (동시에 하나만 존재하므로 코루틴이 알아서 정리)
    private static readonly object TimedFrictionKey = new object();

    // 마찰 배율을 적용 중인 소스(영역 컴포넌트, 타이머 효과 등)를 모아두고
    // 매번 재계산해서 실제 값에 반영 (여러 영역이 겹쳐도 안전)
    private readonly Dictionary<object, float> frictionModifiers = new();
    private readonly Dictionary<object, float> speedModifiers = new();

    public struct MovementEffectData
    {
        public bool ChangeSpeed;
        public float SpeedMultiplier;
        public bool ChangeFriction;
        public float FrictionMultiplier;
        public float Duration;
    }

    private void Awake()
    {
        brain = GetComponent<PlayerBrain>();
    }

    /// <summary>
    /// 오징어 먹물 효과
    /// </summary>
    public void ApplyBlind(float duration)
    {
        if (!PlayerSpawnManager.Instance.IsMine(brain.PlayerId))
            return;
        _ = UIManager.Show<UIBlind>(duration);
    }

    /// <summary>
    /// 버섯 등의 넉백 효과
    /// </summary>
    public void ApplyKnockback(Vector3 force)
    {
        brain.Rb.AddForce(force, ForceMode.Impulse);
    }

    #region Movement (타이머 기반, 예: 함정류)
    /// <summary>
    /// 이동속도/마찰 일정 시간 변경 (Duration 이후 자동 복구)
    /// </summary>
    public void ApplyMovementEffect(MovementEffectData data)
    {
        if (movementCoroutine != null)
            StopCoroutine(movementCoroutine);
        movementCoroutine = StartCoroutine(CoMovement(data));
    }

    private IEnumerator CoMovement(MovementEffectData data)
    {
        PlayerMovement movement = brain.ActionController.Movement;
        float prevSpeedMultiplier = movement.SpeedMultiplier;

        if (data.ChangeSpeed)
        {
            movement.SpeedMultiplier = data.SpeedMultiplier;
        }
        if (data.ChangeFriction)
        {
            ApplyFrictionModifier(TimedFrictionKey, data.FrictionMultiplier);
        }

        yield return new WaitForSeconds(data.Duration);

        if (data.ChangeSpeed)
        {
            movement.SpeedMultiplier = prevSpeedMultiplier;
        }
        if (data.ChangeFriction)
        {
            RemoveFrictionModifier(TimedFrictionKey);
        }

        movementCoroutine = null;
    }
    #endregion

    #region Area (Enter/Exit 기반, 예: EggInside, HoneyLiquid, OnionLiquid)
    /// <summary>
    /// 영역 진입 시 마찰 감소 적용 (EggInside, OnionLiquid)
    /// </summary>
    public void EnterFrictionArea(IngredientTraitArea source, float frictionMultiplier)
    {
        ApplyFrictionModifier(source, frictionMultiplier);
    }

    /// <summary>
    /// 영역 이탈 시 마찰 감소 해제
    /// </summary>
    public void ExitFrictionArea(IngredientTraitArea source)
    {
        RemoveFrictionModifier(source);
    }

    /// <summary>
    /// 영역 최초 진입 시 속도/가속도 0 처리 (HoneyLiquid)
    /// 주의: OnTriggerEnter에서 1회만 호출되어야 함 (Stay에서 호출 금지)
    /// </summary>
    public void ApplyStopOnEnter()
    {
        if (brain == null || brain.Rb == null)
            return;

        brain.Rb.linearVelocity = Vector3.zero;
        brain.Rb.angularVelocity = Vector3.zero;

        // 입력 누적/관성 등 PlayerMovement 내부에 별도의 가속 상태가 있다면 여기서 초기화 필요
        // 예: brain.ActionController.Movement.ResetVelocity();
        // -> PlayerMovement에 해당 메서드가 없다면 추가 구현 필요
    }
    #endregion

    #region Friction Core
    private void ApplyFrictionModifier(object source, float multiplier)
    {
        frictionModifiers[source] = multiplier;
        RecalculateFriction();
    }

    private void RemoveFrictionModifier(object source)
    {
        if (frictionModifiers.Remove(source))
            RecalculateFriction();
    }

    private void RecalculateFriction()
    {
        PlayerMovement movement = brain.ActionController.Movement;

        if (frictionModifiers.Count == 0)
        {
            movement.FrictionMultiplier = 1f;
            return;
        }

        // 여러 영역이 겹칠 경우 가장 강한(가장 낮은 값의) 효과를 적용
        float multiplier = float.MaxValue;
        foreach (float value in frictionModifiers.Values)
        {
            multiplier = Mathf.Min(multiplier, value);
        }
        movement.FrictionMultiplier = multiplier;
        Debug.Log($"[Effect] FrictionMultiplier set to {multiplier}");
    }
    #endregion

    public void EnterSpeedArea(IngredientTraitArea source, float multiplier)
    {
        speedModifiers[source] = multiplier;
        RecalculateSpeed();
    }

    public void ExitSpeedArea(IngredientTraitArea source)
    {
        if (speedModifiers.Remove(source))
            RecalculateSpeed();
    }

    private void RecalculateSpeed()
    {
        PlayerMovement movement = brain.ActionController.Movement;

        if (speedModifiers.Count == 0)
        {
            movement.SpeedMultiplier = 1f;
            return;
        }

        float multiplier = 1f;
        foreach (float value in speedModifiers.Values)
            multiplier = Mathf.Max(multiplier, value); // 겹칠 경우 더 빠른 쪽 우선 (기획 의도에 맞게 Min으로 바꿔도 됨)

        movement.SpeedMultiplier = multiplier;
        Debug.Log($"[Effect] SpeedMultiplier set to {multiplier}");
    }
}