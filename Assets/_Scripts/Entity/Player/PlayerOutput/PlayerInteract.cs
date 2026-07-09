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

        CatchableObj obj = FindInteractTarget<CatchableObj>();
        if (obj == null) return;
        if (obj.IsHold) return;
        if (!obj.CanBePicked) return;

        EntityPickupPacket packet = new()
        {
            EntityId = obj.NetworkId,
            PlayerID = brain.PlayerId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void RequestHeldPrimaryAction()
    {
        if (!IsHolding)
        {
            return;
        }

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
        IHeldObjectReceiver receiver = FindInteractTarget<IHeldObjectReceiver>();
        if (receiver == null) return false;

        return receiver.TryReceiveHeldObject(HeldObj, this);
    }
    
    private void RequestSecondaryAction()
    {
        CatchableObjType objType = IsHolding ? HeldObj.ObjType : CatchableObjType.Default;

        switch (objType)
        {
            case CatchableObjType.Default:
                break;
            default:
                RequestThrow();
                break;
        }
    }

    private void RequestKeyInteract()
    {
        CatchableObjType objType = IsHolding ? HeldObj.ObjType : CatchableObjType.Default;

        switch (objType)
        {
            case CatchableObjType.Default:
                //TODO : 빈손 F키 상호작용 처리 필요
                break;
            case CatchableObjType.Ingredient:
                //TODO : 재료 F키 상호작용 처리 필요
                break;
            case CatchableObjType.Pan:
                //TODO : 프라이팬 F키 상호작용 처리 필요
                break;
            case CatchableObjType.Knife:
                //TODO : 칼 F키 상호작용 처리 필요
                break;
            case CatchableObjType.Plate:
                //TODO : 그릇 F키 상호작용 처리 필요
                break;
            case CatchableObjType.Broom:
                //TODO : 빗자루 F키 상호작용 처리 필요
                break;
            case CatchableObjType.Bucket:
                //TODO : 양동이 F키 상호작용 처리 필요
                break;
        }
    }
    #endregion

    #region User Input Helper
    private void RequestDrop()
    {
        if (!IsHolding) return;

        CatchableObj target = HeldObj;
        HeldObj = null;

        target.transform.SetParent(null, true);
        target.OnDrop();
    }

    private void RequestThrow()
    {
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

        HeldObj = null;

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public bool TryServePlate(PlateInteraction plate)
    {
        if (plate == null) return false;

        IServePlate target = FindInteractTarget<IServePlate>();
        if (target == null) return false;

        return target.TryServePlate(plate);
    }

    public bool TryCutTarget()
    {
        CatchableObj target = FindInteractTarget<CatchableObj>();
        if (target == null) return false;
        if (!target.TryGetComponent(out IngredientReaction ingredientReaction)) return false;

        ingredientReaction.Interact(IngredientAction.Cut);
        return true;
    }

    public bool TryReleaseHeld(CatchableObj target)
    {
        if (target == null) return false;
        if (HeldObj != target) return false;

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

        target.OnPick();
        target.transform.SetParent(brain.ItemHoldParent, false);
        target.transform.localPosition = target.HoldLocalPosition;
        target.transform.localRotation = Quaternion.Euler(target.HoldLocalEulerAngles);
        HeldObj = target;
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
    public T FindInteractTarget<T>() where T : class
    {
        Ray ray = BuildInteractRay();
        RaycastHit[] hits = Physics.SphereCastAll(
            ray.origin,
            GetInteractRadius(),
            ray.direction,
            brain.InteractDistance);

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if (hitCollider == null) continue;
            if (hitCollider.transform.IsChildOf(brain.transform)) continue;

            T target = GetComponentInParent<T>(hitCollider);
            if (target == null) continue;

            return target;
        }

        return null;
    }

    private static T GetComponentInParent<T>(Collider collider) where T : class
    {
        if (typeof(Component).IsAssignableFrom(typeof(T)))
            return collider.GetComponentInParent(typeof(T)) as T;

        foreach (MonoBehaviour behaviour in collider.GetComponentsInParent<MonoBehaviour>())
        {
            if (behaviour is T target)
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

        Vector3 start = origin.position + origin.forward * brain.InteractRayStartOffset;
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
