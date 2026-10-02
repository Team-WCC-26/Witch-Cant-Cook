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
        {
            if (Time.unscaledTime >= nextRequestAt[id]) 
                nextRequestAt.Remove(id);
        }

        Debug.Log($"<color=#87CEFA>[Submit] Bell: platesInZone={plates.Count}</color>", this);

        foreach (PlateInteraction plate in plates)
        {
            SubmitPlateAsync(plate).Forget();
        }
    }

    private async UniTask SubmitPlateAsync(PlateInteraction plate)
    {
        #region 방어 및 로그 코드
        // plate가 null이거나 비어있는 경우
        if (plate == null) 
            return;
        if (plate.IsEmpty) 
        { 
            LogSkipped(plate, "empty plate"); 
            return; 
        }
        // CatchableObj가 없는 경우
        if (!plate.TryGetComponent(out CatchableObj catchable)) 
        { 
            LogSkipped(plate, "CatchableObj missing"); 
            return; 
        }

        // 로컬 소유자가 아닌 경우
        if (!catchable.IsLocalOwner) 
        { 
            LogSkipped(plate, $"not local owner, EntityId={catchable.NetworkId}, LastHolder={catchable.LastHolderPlayerId}"); 
            return; 
        }

        // 유효하지 않은 EntityId인 경우
        if (catchable.NetworkId <= 0) 
        { 
            LogSkipped(plate, $"invalid EntityId={catchable.NetworkId}"); 
            return; 
        }

        // ServerManager가 없는 경우
        ServerManager server = ServerManager.Instance;
        if (server == null) 
        {
            LogSkipped(plate, "ServerManager missing"); 
            return; 
        }

        // 이미 요청 중이거나 쿨다운 중인 경우
        long plateId = catchable.NetworkId;
        if (nextRequestAt.ContainsKey(plateId) || !sending.Add(plateId)) 
        { 
            LogSkipped(plate, $"request in progress/cooldown, EntityId={plateId}"); 
            return; 
        }
        #endregion

        nextRequestAt[plateId] = Time.unscaledTime + RequestCooldown;

        try
        {
            DishOrderManager.ReportSubmission(plate, plateId);
            ServeDishPacket packet = new() { EntityId = plateId };
            Debug.Log($"<color=#87CEFA>[Submit TX REQUEST] C_ServeDish / ServeDishPacket {{ EntityId={packet.EntityId} }} </color>", this);
            await server.SendData(PacketSerializer.Serialize(packet));
            // Leave plate/food alive until ObjectNetworkRouter receives the server destroy event.
            // DishOrderManager removes the order only upon the server's DishState result.
        }
        catch (Exception error)
        {
            nextRequestAt.Remove(plateId);
            DishOrderManager.ReportSubmissionIssue("전송 오류: " + error.Message);
            Debug.LogException(error, this);
        }
        finally { sending.Remove(plateId); }
    }

    private void LogSkipped(PlateInteraction plate, string reason)
    {
        DishOrderManager.ReportSubmissionIssue($"요청 생략: {plate.name} / {reason}");
        Debug.Log($"<color=#87CEFA>[Submit SKIP] plate={plate.name}: {reason}</color>", this);
    }
}
