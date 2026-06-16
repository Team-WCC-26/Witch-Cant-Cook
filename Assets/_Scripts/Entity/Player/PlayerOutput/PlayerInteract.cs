using Protocol;
using Server;
using System;
using UnityEngine;

public class PlayerInteract
{
    private readonly PlayerBrain brain;

    private const bool DebugInteraction = true;

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

        CatchableObj obj = FindCatchable();
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
        CatchableObjType objType = IsHolding ? HeldObj.ObjType : CatchableObjType.Default;

        switch (objType)
        {
            case CatchableObjType.Default:
                Debug.Log("주먹질");
                break;
            case CatchableObjType.Knife:
                //재료 자르기
                CatchableObj target = FindCatchable();
                if (target == null) break;
                if (!target.TryGetComponent(out IngredientReaction ingredientReaction))
                    break;

                ingredientReaction.Interact(IngredientAction.Cut);
                break;
            default:
                RequestDrop();
                break;
        }
    }
    
    private void RequestSecondaryAction()
    {
        CatchableObjType objType = IsHolding ? HeldObj.ObjType : CatchableObjType.Default;

        switch (objType)
        {
            case CatchableObjType.Default:
                Debug.Log("아무일도... 없었다!");
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

    #region Interact Ray
    private CatchableObj FindCatchable()
    {
        RaycastHit[] hits = Physics.RaycastAll(BuildInteractRay(), brain.InteractDistance);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null) continue;
            if (hitCollider.transform.IsChildOf(brain.transform)) continue;

            if (!hitCollider.TryGetComponent(out CatchableObj catchable))
            {
                DebugLog($"Hit non-catchable object: {hitCollider.name}");
                continue;
            }

            DebugLog($"Hit catchable object: {catchable.name}");
            return catchable;
        }

        return null;
    }

    private Ray BuildInteractRay()
    {
        Transform origin = brain.PlayerCam != null
            ? brain.PlayerCam.transform
            : brain.transform;

        Vector3 start = origin.position + origin.forward * brain.InteractRayStartOffset;
        return new Ray(start, origin.forward);
    }

    private void DrawDebugInteractRay()
    {
        if (!DebugInteraction) return;

        Ray ray = BuildInteractRay();
        Debug.DrawLine(ray.origin, ray.origin + ray.direction * brain.InteractDistance, Color.red);
    }

    private static void DebugLog(string message)
    {
        if (!DebugInteraction) return;

        Debug.Log($"[PlayerInteract] {message}");
    }
    #endregion
}
