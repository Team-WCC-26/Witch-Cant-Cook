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

    private PacketId _pickId => PacketId.S_IngredientPickup;

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
                DebugLog("Secondary clicked. Throw held object.");
                RequestThrow();
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

        IngredientPickupPacket packet = new()
        {
            EntityId = obj.EntityId,
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

        IngredientThrowPacket packet = new()
        {
            EntityId = target.EntityId,
            Position = ToNumericsVector3(brain.ItemHoldParent.position),
            Velocity = ToNumericsVector3(velocity)
        };

        HeldObj = null;

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void OnPicked(ReadOnlyMemory<byte> data)
    {
        IngredientPickupPacket packet =
            MemoryPackSerializer.Deserialize<IngredientPickupPacket>(data.Span);

        if (!GameManager.Instance.catchableDics.TryGetValue(packet.EntityId, out CatchableObj target))
            return;

        Debug.LogError($"packetID : {packet.PlayerID}, brainID : {brain.PlayerId}");
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

            CatchableObj obj = hitCollider.GetComponentInParent<CatchableObj>();
            DebugLog(obj != null
                ? $"Hit catchable object: {obj.name}"
                : $"Hit non-catchable object: {hitCollider.name}");

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

    private static System.Numerics.Vector3 ToNumericsVector3(Vector3 value)
    {
        return new System.Numerics.Vector3(value.x, value.y, value.z);
    }

}
