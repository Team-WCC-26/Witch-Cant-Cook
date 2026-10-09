using System;
using UnityEngine;

/// <summary>
/// 한 물체의 배치와 충격에 따른 해제를 관리한다.
/// 소유한 Interaction은 비활성화 시 Dispose를 호출해 구독을 정리한다.
/// </summary>
public sealed class ObjPlacement : IDisposable
{
    private CatchableObj target;
    private ColliderRelay relay;
    private float releaseImpulse = float.MaxValue;
    private Vector3 placedPosition;
    private Quaternion placedRotation;

    public CatchableObj Target => target;
    public bool IsPlaced => target != null;

    // 슬롯 소유자에게 위치이동 알림
    public event Action<CatchableObj> Released;

    /// <summary>
    /// 해제 알림 없이 구독을 정리한다. 이후 다시 배치할 수 있다.
    /// </summary>
    public void Dispose() => ClearSubscriptions();

    /// <summary>
    /// 위치·회전과 속도를 보정하고 충격에 따른 배치 해제를 감지한다.
    /// </summary>
    public bool TryPlace(CatchableObj item, Transform slot, float? minReleaseImpulse = null)
    {
        if (IsPlaced || item == null || slot == null || item.IsHold) return false;
        if (item.Rb == null || item.Col == null || item.Col.isTrigger) return false;
        if (minReleaseImpulse.HasValue &&
            (minReleaseImpulse.Value <= 0f || float.IsNaN(minReleaseImpulse.Value) ||
             float.IsInfinity(minReleaseImpulse.Value))) return false;
        if (!item.TryGetComponent(out ColliderRelay itemRelay)) return false;
        if (!itemRelay.isActiveAndEnabled || !item.gameObject.activeInHierarchy) return false;

        // 이벤트 연결 정리
        ClearSubscriptions();
        target = item;
        relay = itemRelay;
        if (minReleaseImpulse.HasValue) releaseImpulse = minReleaseImpulse.Value;

        item.SetPhysicsState(true);
        item.ChangePickState(true);
        placedPosition = slot.position;
        placedRotation = slot.rotation;
        RestorePlacement();

        relay.CollisionEntered += HandleCollision;
        relay.CollisionStayed += HandleCollision;
        item.OnPicked += Release;
        item.OnDropped += Release;
        return true;
    }

    /// <summary>
    /// 배치 상태와 구독만 해제한다. 물리 충격과 현재 속도는 유지한다.
    /// </summary>
    public void Release()
    {
        CatchableObj released = target;
        ClearSubscriptions();
        if (released != null) Released?.Invoke(released);
    }

    public void SetReleaseImpulse(float impulse)
    {
        if (impulse <= 0f || float.IsNaN(impulse) || float.IsInfinity(impulse)) return;
        releaseImpulse = impulse;
    }

    private void ClearSubscriptions()
    {
        if (relay != null)
        {
            relay.CollisionEntered -= HandleCollision;
            relay.CollisionStayed -= HandleCollision;
        }
        if (target != null)
        {
            target.OnPicked -= Release;
            target.OnDropped -= Release;
        }

        target = null;
        relay = null;
    }

    private void HandleCollision(Collision collision)
    {
        if (!IsPlaced || collision == null) return;
        if (collision.impulse.magnitude >= releaseImpulse)
        {
            Release();
            return;
        }

        RestorePlacement();
    }

    /// <summary>
    /// 저장한 배치 좌표로 복원하고 이동·회전 속도를 초기화한다.
    /// </summary>
    private void RestorePlacement()
    {
        if (!IsPlaced || target.Rb == null) return;
        target.transform.SetPositionAndRotation(placedPosition, placedRotation);
        target.Rb.position = placedPosition;
        target.Rb.rotation = placedRotation;
        target.Rb.linearVelocity = Vector3.zero;
        target.Rb.angularVelocity = Vector3.zero;
    }
}
