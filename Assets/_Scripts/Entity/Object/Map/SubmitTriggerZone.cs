using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Protocol;
using Server;
using UnityEngine;

public class SubmitTriggerZone : MonoBehaviour
{
    // Track each collider so exiting one child collider does not remove a plate still in the zone.
    private readonly Dictionary<Collider, PlateInteraction> overlaps = new();
    private readonly Dictionary<long, float> nextRequestAt = new();
    private readonly HashSet<long> sending = new();
    private const float RequestCooldown = 1f;

    private void OnEnable() => Bell.OnBellRung += Submit;

    private void OnDisable()
    {
        Bell.OnBellRung -= Submit;
        overlaps.Clear();
        nextRequestAt.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        PlateInteraction plate = other.GetComponentInParent<PlateInteraction>();
        if (plate != null) overlaps[other] = plate;
    }

    private void OnTriggerExit(Collider other) => overlaps.Remove(other);

    private void Submit()
    {
        var plates = new HashSet<PlateInteraction>();
        foreach (var pair in new List<KeyValuePair<Collider, PlateInteraction>>(overlaps))
        {
            if (pair.Key == null || !pair.Key.enabled || !pair.Key.gameObject.activeInHierarchy ||
                pair.Value == null || !pair.Value.gameObject.activeInHierarchy)
            {
                overlaps.Remove(pair.Key);
                continue;
            }
            plates.Add(pair.Value);
        }
        foreach (long id in new List<long>(nextRequestAt.Keys))
            if (Time.unscaledTime >= nextRequestAt[id]) nextRequestAt.Remove(id);
        Debug.Log($"<color=#87CEFA>[Submit] Bell: platesInZone={plates.Count}</color>", this);
        foreach (PlateInteraction plate in plates) SubmitPlateAsync(plate).Forget();
    }

    private async UniTask SubmitPlateAsync(PlateInteraction plate)
    {
        if (plate == null) return;
        if (plate.IsEmpty) { LogSkipped(plate, "empty plate"); return; }
        if (!plate.TryGetComponent(out CatchableObj catchable)) { LogSkipped(plate, "CatchableObj missing"); return; }
        // The last holder is the authority even after dropping the plate on the counter.
        if (!catchable.IsLocalOwner) { LogSkipped(plate, $"not local owner, EntityId={catchable.NetworkId}, LastHolder={catchable.LastHolderPlayerId}"); return; }
        if (catchable.NetworkId <= 0) { LogSkipped(plate, $"invalid EntityId={catchable.NetworkId}"); return; }
        ServerManager server = ServerManager.Instance;
        if (server == null) { LogSkipped(plate, "ServerManager missing"); return; }

        long plateId = catchable.NetworkId;
        if (nextRequestAt.ContainsKey(plateId) || !sending.Add(plateId)) { LogSkipped(plate, $"request in progress/cooldown, EntityId={plateId}"); return; }
        nextRequestAt[plateId] = Time.unscaledTime + RequestCooldown;
        try
        {
            ServeDishPacket packet = new() { EntityId = plateId };
            Debug.Log($"<color=#87CEFA>[Submit TX REQUEST] C_ServeDish / ServeDishPacket {{ EntityId={packet.EntityId} }} plate={plate.name}</color>", this);
            await server.SendData(PacketSerializer.Serialize(packet));
            // Leave plate/food alive until ObjectNetworkRouter receives the server destroy event.
            // DishOrderManager removes the order only upon the server's DishState result.
        }
        catch (Exception error)
        {
            nextRequestAt.Remove(plateId);
            Debug.LogException(error, this);
        }
        finally { sending.Remove(plateId); }
    }

    private void LogSkipped(PlateInteraction plate, string reason) =>
        Debug.Log($"<color=#87CEFA>[Submit SKIP] plate={plate.name}: {reason}</color>", this);
}
