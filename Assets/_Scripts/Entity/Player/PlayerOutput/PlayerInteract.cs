using MemoryPack;
using Protocol;
using Server;
using System;
using UnityEngine;

public class PlayerInteract
{
    private readonly PlayerBrain brain;

    private const float DefaultThrowForce = 8.0f;
    private const bool DebugInteraction = true;

    public CatchableObj HeldObj { get; private set; }
    public bool IsHolding => HeldObj != null;

    private PacketId _pickId => PacketId.S_EntityPickup;

    public PlayerInteract(PlayerBrain brain)
    {
        this.brain = brain;
        ServerManager.Instance.RegisterHandler(_pickId, OnPicked);
    }

    public void Handle(PlayerInteraction interaction)
    {
        DrawDebugInteractRay();

        if (!PlayerSpawnManager.Instance.IsMine(brain.PlayerId)) return;

        switch (interaction)
        {
            case PlayerInteraction.Pick:
                DebugLog("Primary clicked. Try pick.");
                RequestPick();
                break;
            case PlayerInteraction.Drop:
                //DebugLog("Primary clicked. Drop held object.");
                //Drop();
                break;
            case PlayerInteraction.Throw:
                DebugLog("Secondary clicked. Use held object.");
                RequestSecondaryAction();
                break;
            case PlayerInteraction.Use:
                DebugLog("Interact key pressed. Use held object.");
                RequestUseAction();
                break;
        }
    }

    private void RequestPick()
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

    private void Drop()
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

        Vector3 velocity = GetAimDirection() * GetThrowForce(target);

        EntityThrowPacket packet = new()
        {
            EntityId = target.NetworkId,
            Position = ProtocolTypeConverter.ToNumericsVector3(brain.ItemHoldParent.position),
            Velocity = ProtocolTypeConverter.ToNumericsVector3(velocity)
        };

        HeldObj = null;

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void RequestSecondaryAction()
    {
        CatchableObjType objType = IsHolding ? HeldObj.ObjType : CatchableObjType.Default;

        switch (objType)
        {
            case CatchableObjType.Default:
                //TODO : 빈손 우클릭 주먹질 처리 필요
                RequestThrow();
                Debug.Log("Interact : 주먹질");
                break;
            case CatchableObjType.Ingredient:
                RequestThrow();
                Debug.Log("Interact : 던지기");
                break;
            case CatchableObjType.Pan:
                //TODO : 프라이팬 우클릭 휘두르기 처리 필요
                RequestThrow();
                Debug.Log("Interact : 프라이팬");
                break;
            case CatchableObjType.Knife:
                Debug.Log("Interact : 칼");

                //TODO : 칼 우클릭 휘두르기 모션 필요

                //재료 자르기
                CatchableObj target = FindCatchable();
                if (target == null) break;
                if (!target.TryGetComponent(out IngredientReaction ingredientReaction))
                    break;

                ingredientReaction.Interact(IngredientAction.Cut);
                break;
            case CatchableObjType.Plate:
                RequestThrow();
                Debug.Log("Interact : 그릇");
                break;
            case CatchableObjType.Broom:
                //TODO : 빗자루 우클릭 도구 행동 처리 필요
                RequestThrow();
                Debug.Log("Interact : 빗자루");
                break;
            case CatchableObjType.Bucket:
                //TODO : 양동이 우클릭 도구 행동 처리 필요
                RequestThrow();
                Debug.Log("Interact : 양동이");
                break;
        }
    }

    private void RequestUseAction()
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

    private void OnPicked(ReadOnlyMemory<byte> data)
    {
        EntityPickupPacket packet =
            PacketSerializer.Deserialize<EntityPickupPacket>(data.Span);

        if (!NetworkObjectRegistry.Instance.TryGet(packet.EntityId, out CatchableObj target))
            return;

        if (packet.PlayerID != brain.PlayerId) return;

        target.OnPick();
        target.transform.SetParent(brain.ItemHoldParent, false);
        target.transform.localPosition = target.HoldLocalPosition;
        target.transform.localRotation = Quaternion.Euler(target.HoldLocalEulerAngles);
        HeldObj = target;
    }

    private Ray BuildInteractRay()
    {
        Transform origin = brain.PlayerCam != null
            ? brain.PlayerCam.transform
            : brain.transform;

        Vector3 start = origin.position + origin.forward * brain.InteractRayStartOffset;
        return new Ray(start, origin.forward);
    }

    private CatchableObj FindCatchable()
    {
        RaycastHit[] hits = Physics.RaycastAll(BuildInteractRay(), brain.InteractDistance);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null) continue;
            if (hitCollider.transform.IsChildOf(brain.transform)) continue;

            CatchableObj obj = hitCollider.GetComponent<CatchableObj>();
            if (obj == null)
            {
                DebugLog($"Hit non-catchable object: {hitCollider.name}");
                continue;
            }

            DebugLog($"Hit catchable object: {obj.name}");
            return obj;
        }

        return null;
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

    private Vector3 GetAimDirection()
    {
        Transform origin = brain.PlayerCam != null
            ? brain.PlayerCam.transform
            : brain.transform;

        return origin.forward.normalized;
    }

    private float GetThrowForce(CatchableObj target)
    {
        return target.ThrowForce > 0.0f ? target.ThrowForce : DefaultThrowForce;
    }
}
