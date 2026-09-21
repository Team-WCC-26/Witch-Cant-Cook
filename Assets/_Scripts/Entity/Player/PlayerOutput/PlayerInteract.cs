using Protocol;
using Server;
using System;
using UnityEngine;

public class PlayerInteract
{
    private readonly PlayerBrain brain;

    public CatchableObj HeldObj { get; private set; }
    public bool IsHolding => HeldObj != null;

    public PlayerInteract(PlayerBrain brain)
    {
        this.brain = brain;
    }

    public void Handle(PlayerInteraction interaction)
    {
        DrawDebugInteractRay();

        if (!PlayerSpawnManager.Instance.IsMine(brain.PlayerId)) return;

        switch (interaction)
        {
            case PlayerInteraction.DefaultPrimary:
                RequestDefaultPrimaryAction();
                break;
            case PlayerInteraction.HeldPrimary:
                RequestHeldPrimaryAction();
                break;
            case PlayerInteraction.Secondary:
                RequestSecondaryAction();
                break;
            case PlayerInteraction.KeyInteract:
                RequestKeyInteract();
                break;
        }
    }

    #region Request User Input Action
    private void RequestDefaultPrimaryAction()
    {
        if (IsHolding) return;

        IInteractTarget target = FindInteractTarget();
        if (target is not CatchableObj obj)
        {
            brain.ActionController.PunchAction();
            target.GetTargetComponent<IPunchReceiver>()?.OnPunched(brain);
            return;
        }
        if (obj.IsHold) return;
        if (!obj.CanBePicked) return;

        RequestEntityInteract(obj.NetworkId);
    }

    private void RequestHeldPrimaryAction()
    {
        if (!IsHolding) return;

        if (TryUseHeldPrimaryAction()) return;
        if (TryUseHeldObjectReceiver()) return;

        RequestDrop();
    }

    private bool TryUseHeldPrimaryAction()
    {
        if (TryGetHeldComponent(out IHeldPrimaryAction action))
            return action.TryUsePrimary(this);

        return false;
    }

    private bool TryUseHeldObjectReceiver()
    {
        IInteractTarget target = FindInteractTarget();
        IHeldObjectReceiver receiver = target.GetTargetComponent<IHeldObjectReceiver>();
        if (receiver == null) return false;

        return receiver.TryReceiveHeldObject(HeldObj, this);
    }
    
    private void RequestSecondaryAction()
    {
        EntityCategory category = IsHolding ? HeldObj.Category : EntityCategory.Player;

        switch (category)
        {
            case EntityCategory.Player:
                break;
            default:
                RequestThrow();
                break;
        }
    }

    private void RequestKeyInteract()
    {
        EntityCategory category = IsHolding ? HeldObj.Category : EntityCategory.Player;

        switch (category)
        {
            case EntityCategory.Player:
                //TODO : 빈손 F키 상호작용 처리 필요
                break;
            case EntityCategory.Ingredient:
                //TODO : 재료 F키 상호작용 처리 필요
                break;
            case EntityCategory.Pan:
                //TODO : 프라이팬 F키 상호작용 처리 필요
                break;
            case EntityCategory.Knife:
                //TODO : 칼 F키 상호작용 처리 필요
                break;
            case EntityCategory.Plate:
                //TODO : 그릇 F키 상호작용 처리 필요
                break;
            case EntityCategory.Broom:
                //TODO : 빗자루 F키 상호작용 처리 필요
                break;
            case EntityCategory.Bucket:
                //TODO : 양동이 F키 상호작용 처리 필요
                break;
        }
    }
    #endregion

    #region User Input Helper
    private void RequestDrop()
    {
        RequestDropHeld();
    }

    public void RequestDropHeld()
    {
        if (!IsHolding) return;

        CatchableObj target = HeldObj;
        EntityThrowPacket packet = new()
        {
            EntityId = target.NetworkId,
            Position = ProtocolTypeConverter.ToNumericsVector3(target.transform.position),
            Velocity = System.Numerics.Vector3.Zero
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void ForceDropHeld()
    {
        RequestDropHeld();
    }

    private void RequestThrow()
    {
        brain.ActionController.CancelAction();
        if (!IsHolding) return;

        CatchableObj target = HeldObj;

        Transform throwOrigin = GetThrowOrigin();
        Vector3 throwPosition = GetThrowPosition(throwOrigin);
        Vector3 velocity = GetThrowDirection(throwOrigin) * GetThrowForce();

        EntityThrowPacket packet = new()
        {
            EntityId = target.NetworkId,
            Position = ProtocolTypeConverter.ToNumericsVector3(throwPosition),
            Velocity = ProtocolTypeConverter.ToNumericsVector3(velocity)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void RequestEntityInteract(long targetEntityId)
    {
        EntityInteractPacket packet = new()
        {
            TargetEntityId = targetEntityId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public bool TryUseEquipment()
    {
        if (!IsHolding) return false;
        if (!HeldObj.IsEquipment) return false;

        return brain.ActionController.TryEquipAction();
    }

    public bool TryReleaseHeld(CatchableObj target)
    {
        if (target == null) return false;
        if (HeldObj != target) return false;

        brain.ActionController.CancelAction();
        HeldObj = null;
        target.transform.SetParent(null, true);
        return true;
    }

    private bool TryGetHeldComponent<T>(out T component) where T : class
    {
        component = null;

        if (!IsHolding) return false;

        foreach (MonoBehaviour behaviour in HeldObj.GetComponents<MonoBehaviour>())
        {
            if (behaviour is not T target) continue;

            component = target;
            return true;
        }

        return false;
    }
    #endregion

    #region Actual Interaction
    public void ApplyPicked(CatchableObj target)
    {
        if (target == null) return;

        brain.ActionController.CancelAction();
        target.OnPick(brain);
        Transform holdParent = target.IsEquipment
            ? brain.EquipPoint
            : brain.ItemPoint;

        target.transform.SetParent(holdParent, false);
        LocalTransformData holdTransform = target.HoldTransform;
        target.transform.localPosition = holdTransform.LocalPosition;
        target.transform.localRotation = Quaternion.Euler(holdTransform.LocalEulerAngles);
        HeldObj = target;
    }

    public void ApplyThrown(CatchableObj target)
    {
        if (target == null || HeldObj != target) return;

        brain.ActionController.CancelAction();
        HeldObj = null;
    }
    #endregion

    #region Throw Action Helper
    private Transform GetThrowOrigin()
    {
        return brain.PlayerCam != null
            ? brain.PlayerCam.transform
            : brain.transform;
    }

    private Vector3 GetThrowPosition(Transform origin)
    {
        return origin.TransformPoint(brain.ThrowCameraOffset);
    }

    private Vector3 GetThrowDirection(Transform origin)
    {
        Quaternion angleOffset = Quaternion.AngleAxis(-brain.ThrowAngle, origin.right);
        return (angleOffset * origin.forward).normalized;
    }

    private float GetThrowForce()
    {
        return brain.ThrowForce;
    }
    #endregion

    #region Find Interactable Target
    public IInteractTarget FindInteractTarget()
    {
        if (brain.InteractDistance <= 0f)
        {
            throw new InvalidOperationException("InteractDistance must be greater than 0.");
        }

        Ray ray = BuildInteractRay();
        RaycastHit[] hits = Physics.SphereCastAll(
            ray.origin,
            GetInteractRadius(),
            ray.direction,
            brain.InteractDistance,
            brain.InteractLayerMask,
            QueryTriggerInteraction.Collide); // trigger collider도 상호작용 가능하도록 설정

        IInteractTarget bestTarget = null;
        float bestScore = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if (hitCollider.transform.IsChildOf(brain.transform)) continue; // Ignore self-collisions

            // Check if IInteractTarget is acceptable
            IInteractTarget target = GetInteractTargetInParent(hitCollider);
            if (target == null) continue;
            if (!target.Accepts(GetSourceCategory())) continue;

            float score = GetTargetScore(ray, hitCollider, hit.distance);

            if (score >= bestScore) continue;

            bestTarget = target;
            bestScore = score;
        }

        return bestTarget;
    }

    private EntityCategory GetSourceCategory()
    {
        return HeldObj != null
            ? HeldObj.Category
            : EntityCategory.Player;
    }

    private float GetTargetScore(Ray ray, Collider targetCollider, float distance)
    {
        Vector3 targetCenter = targetCollider.bounds.center;
        Vector3 castCenter = ray.GetPoint(Mathf.Max(0f, Vector3.Dot(targetCenter - ray.origin, ray.direction)));

        //ray 중심으로부터 얼마나 벗어났는가
        float offset = Vector3.Distance(castCenter, targetCenter);
        float offsetScore = Mathf.Clamp01(offset / GetInteractRadius());

        //ray 시작점과의 거리
        float distanceRatio = Mathf.Clamp01(distance / brain.InteractDistance);
        int distanceScore = Mathf.Min(Mathf.FloorToInt(distanceRatio * 3f), 2);

        float targetScore = offsetScore + distanceScore;
        return targetScore;
    }

    // Collider의 부모 계층에서 상호작용 대상을 찾는다.
    private static IInteractTarget GetInteractTargetInParent(Collider collider)
    {
        foreach (MonoBehaviour behaviour in collider.GetComponentsInParent<MonoBehaviour>())
        {
            if (behaviour is IInteractTarget target)
                return target;
        }

        return null;
    }

    #endregion

    #region Interact Ray
    private Ray BuildInteractRay()
    {
        Transform origin = brain.PlayerCam != null
            ? brain.PlayerCam.transform
            : brain.transform;

        Vector3 start = origin.position + origin.TransformDirection(brain.InteractRayStartOffset);
        return new Ray(start, origin.forward);
    }

    private float GetInteractRadius()
    {
        return Mathf.Max(0.01f, brain.InteractRadius);
    }

    private void DrawDebugInteractRay()
    {
        if (!brain.DebugInteraction) return;

        Ray ray = BuildInteractRay();
        float radius = GetInteractRadius();
        Vector3 end = ray.origin + ray.direction * brain.InteractDistance;

        DrawDebugCircle(ray.origin, ray.direction, radius, Color.red);
        DrawDebugCircle(end, ray.direction, radius, Color.red);
        DrawDebugSphereCastEdges(ray.origin, end, ray.direction, radius, Color.red);
    }

    private static void DrawDebugSphereCastEdges(Vector3 start, Vector3 end, Vector3 direction, float radius, Color color)
    {
        BuildCircleBasis(direction, out Vector3 right, out Vector3 up);

        Debug.DrawLine(start + right * radius, end + right * radius, color);
        Debug.DrawLine(start - right * radius, end - right * radius, color);
        Debug.DrawLine(start + up * radius, end + up * radius, color);
        Debug.DrawLine(start - up * radius, end - up * radius, color);
    }

    private static void DrawDebugCircle(Vector3 center, Vector3 direction, float radius, Color color)
    {
        const int SegmentCount = 24;

        BuildCircleBasis(direction, out Vector3 right, out Vector3 up);
        Vector3 previous = center + right * radius;

        for (int i = 1; i <= SegmentCount; i++)
        {
            float angle = i * Mathf.PI * 2.0f / SegmentCount;
            Vector3 next = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            Debug.DrawLine(previous, next, color);
            previous = next;
        }
    }

    private static void BuildCircleBasis(Vector3 direction, out Vector3 right, out Vector3 up)
    {
        right = Vector3.Cross(direction, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.Cross(direction, Vector3.forward);
        }

        right.Normalize();
        up = Vector3.Cross(right, direction).normalized;
    }
    #endregion
}
