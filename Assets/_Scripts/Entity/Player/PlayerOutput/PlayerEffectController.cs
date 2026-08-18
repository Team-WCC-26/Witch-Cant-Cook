using System.Collections;
using UnityEngine;

public class PlayerEffectController : MonoBehaviour
{
    private PlayerBrain brain;

    private Coroutine movementCoroutine;

    private float originalSpeed;
    private float originalRunMultiplier;

    [System.Serializable]
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
        Debug.Log($"brain : {brain}");
        Debug.Log($"spawnManager : {PlayerSpawnManager.Instance}");
        Debug.Log($"playerId : {brain?.PlayerId}");

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


    #region Movement
    /// <summary>
    /// 이동속도 / 마찰 변경 효과.
    /// Duration이 지나면 자동으로 원래 값으로 복구된다.
    /// 지역(Trigger) 기반 효과처럼 영역을 벗어날 때 즉시 끝내고 싶다면 EndMovementEffect를 사용한다.
    /// </summary>
    public void ApplyMovementEffect(MovementEffectData data)
    {
        if (movementCoroutine != null)
            StopCoroutine(movementCoroutine);

        movementCoroutine = StartCoroutine(CoMovement(data));
    }

    private IEnumerator CoMovement(MovementEffectData data)
    {
        PlayerMovement movement =
            brain.ActionController.Movement;

        float prevSpeedMultiplier = movement.SpeedMultiplier;
        float prevFrictionMultiplier = movement.FrictionMultiplier;

        if (data.ChangeSpeed)
        {
            movement.SpeedMultiplier = data.SpeedMultiplier;
        }

        if (data.ChangeFriction)
        {
            movement.FrictionMultiplier = data.FrictionMultiplier;
        }

        yield return new WaitForSeconds(data.Duration);

        if (data.ChangeSpeed)
        {
            movement.SpeedMultiplier = prevSpeedMultiplier;
        }

        if (data.ChangeFriction)
        {
            movement.FrictionMultiplier = prevFrictionMultiplier;
        }

        movementCoroutine = null;
    }

    /// <summary>
    /// 진행 중인 이동 효과(ApplyMovementEffect)를 Duration과 무관하게 즉시 종료하고
    /// 속도/마찰 배율을 기본값(1)으로 되돌린다.
    /// 지역(Trigger) 기반 효과에서 플레이어가 영역을 벗어났을 때 사용한다.
    /// </summary>
    public void EndMovementEffect()
    {
        if (movementCoroutine == null)
            return;

        StopCoroutine(movementCoroutine);
        movementCoroutine = null;

        PlayerMovement movement = brain.ActionController.Movement;
        movement.SpeedMultiplier = 1f;
        movement.FrictionMultiplier = 1f;
    }

    #endregion
}