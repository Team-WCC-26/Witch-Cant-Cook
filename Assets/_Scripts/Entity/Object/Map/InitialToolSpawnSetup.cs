using System;
using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;

// Requests initial tools once per kitchen entry. The server assigns IDs and broadcasts spawns.
[DisallowMultipleComponent]
public sealed class InitialToolSpawnSetup : MonoBehaviour
{
    [Serializable]
    public sealed class ToolPlacement
    {
        [Tooltip("초기 배치할 이동식 도구. 현재 지원: KitchenKnife, FryingPan, Plate")]
        public Define.eToolId Tool = Define.eToolId.KitchenKnife;
        [Tooltip("배열 길이가 스폰 수량입니다. 각 Transform의 월드 위치와 회전을 사용합니다.")]
        public Transform[] SpawnPoints = Array.Empty<Transform>();
        public int Count => SpawnPoints?.Length ?? 0;
    }

    // A value snapshot: moving a spawn marker later does not change an already collected plan.
    public readonly struct ToolSpawn
    {
        public Define.eToolId Tool { get; }
        public int ToolId => (int)Tool;
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        public ToolSpawn(Define.eToolId tool, Vector3 position, Quaternion rotation)
        {
            Tool = tool;
            Position = position;
            Rotation = rotation;
        }
    }

    [SerializeField] private ToolPlacement[] placements =
    {
        new() { Tool = Define.eToolId.KitchenKnife, SpawnPoints = new Transform[3] },
        new() { Tool = Define.eToolId.FryingPan, SpawnPoints = new Transform[2] },
        new() { Tool = Define.eToolId.Plate, SpawnPoints = new Transform[5] }
    };
    [SerializeField, Min(0.01f)] private float gizmoRadius = 0.12f;
    private RoomHudSession room;
    private bool requestedForKitchen;
    private int requestGeneration;

    private void OnEnable()
    {
        room = RoomHudSession.Instance;
        room.RoomJoined += ResetRequests;
        room.LobbyMoveReceived += ResetRequests;
        DoorController.DoorOpened += OnDoorOpened;
    }

    private void OnDisable()
    {
        DoorController.DoorOpened -= OnDoorOpened;
        if (room != null)
        {
            room.RoomJoined -= ResetRequests;
            room.LobbyMoveReceived -= ResetRequests;
        }
        requestGeneration++;
    }

    private void ResetRequests()
    {
        requestGeneration++;
        requestedForKitchen = false;
    }

    private async void OnDoorOpened(DoorId door)
    {
        if (door == DoorId.Lobby) { ResetRequests(); return; }
        if (door != DoorId.Kitchen || requestedForKitchen) return;

        LobbyHudState lobby = room.Lobby;
        if (lobby.Players.Count == 0 || string.IsNullOrEmpty(lobby.LocalPlayerId))
        {
            Debug.LogWarning("[Initial Tool Spawns] 참가자 정보가 없어 요청하지 않았습니다.", this);
            return;
        }
        // The first participant is the existing room member on both clients.
        if (lobby.Players[0] != lobby.LocalPlayerId) return;
        if (FindObjectsByType<InitialToolSpawnSetup>(FindObjectsSortMode.None).Length != 1)
        {
            Debug.LogError("[Initial Tool Spawns] 씬에 활성 InitialToolSpawnSetup을 하나만 두세요.", this);
            return;
        }
        if (!TryGetSpawns(out ToolSpawn[] spawns, out string issue))
        {
            Debug.LogWarning($"[Initial Tool Spawns] 요청 취소: {issue}", this);
            return;
        }
        ServerManager server = ServerManager.Instance;
        if (server == null) return;

        // Reserve before awaiting. Do not retry ambiguous sends or duplicate door packets.
        requestedForKitchen = true;
        int generation = requestGeneration;
        try
        {
            foreach (ToolSpawn spawn in spawns)
            {
                if (this == null || !isActiveAndEnabled || generation != requestGeneration) return;
                var packet = new ToolSpawnPacket
                {
                    EntityId = 0,
                    ToolId = spawn.ToolId,
                    Position = ProtocolTypeConverter.ToNumericsVector3(spawn.Position),
                    Quaternion = ProtocolTypeConverter.ToNumericsQuaternion(spawn.Rotation)
                };
                await server.SendData(PacketSerializer.Serialize(packet));
            }
            Debug.Log($"[Initial Tool Spawns] C_ToolSpawn {spawns.Length}개 전송 완료. 서버 스폰 알림으로 생성합니다.", this);
        }
        catch (Exception error)
        {
            Debug.LogWarning("[Initial Tool Spawns] 일부 요청의 전송을 확인할 수 없습니다. 중복 생성을 막기 위해 자동 재전송하지 않습니다.", this);
            Debug.LogException(error, this);
        }
    }

    /// <summary>
    /// Collects the current world-space plan in Inspector order. Invalid configuration
    /// returns an empty array, so callers cannot accidentally send a partial setup.
    /// </summary>
    public bool TryGetSpawns(out ToolSpawn[] spawns, out string issue)
    {
        spawns = Array.Empty<ToolSpawn>();
        issue = null;
        if (placements == null || placements.Length == 0)
        {
            issue = "초기 도구 배치 항목이 없습니다.";
            return false;
        }

        var result = new List<ToolSpawn>();
        var usedPoints = new HashSet<Transform>();
        for (int i = 0; i < placements.Length; i++)
        {
            ToolPlacement placement = placements[i];
            if (placement == null)
            {
                issue = $"배치 항목 {i + 1}이 비어 있습니다.";
                return false;
            }
            if (!Define.TryGetEntityCategory(placement.Tool, out _))
            {
                issue = $"{placement.Tool}: 현재 도구 스폰 경로는 칼·팬·접시만 지원합니다.";
                return false;
            }
            for (int j = 0; j < placement.Count; j++)
            {
                Transform point = placement.SpawnPoints[j];
                if (point == null)
                {
                    issue = $"{placement.Tool}: 스폰 위치 {j + 1}을 지정하세요.";
                    return false;
                }
                if (!usedPoints.Add(point))
                {
                    issue = $"스폰 위치 '{point.name}'이 중복 지정되었습니다.";
                    return false;
                }
                result.Add(new ToolSpawn(placement.Tool, point.position, point.rotation));
            }
        }
        if (result.Count == 0)
        {
            issue = "최소 한 개의 도구 스폰 위치를 지정하세요.";
            return false;
        }
        spawns = result.ToArray();
        return true;
    }

    [ContextMenu("Validate Initial Tool Spawns")]
    private void ValidateConfiguration()
    {
        if (TryGetSpawns(out ToolSpawn[] spawns, out string issue))
            Debug.Log($"[Initial Tool Spawns] 배치 설정 정상: 총 {spawns.Length}개", this);
        else Debug.LogWarning($"[Initial Tool Spawns] {issue}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (placements == null) return;
        foreach (ToolPlacement placement in placements)
        {
            if (placement?.SpawnPoints == null) continue;
            Gizmos.color = placement.Tool switch
            {
                Define.eToolId.KitchenKnife => Color.cyan,
                Define.eToolId.FryingPan => Color.yellow,
                Define.eToolId.Plate => Color.green,
                _ => Color.red
            };
            foreach (Transform point in placement.SpawnPoints)
            {
                if (point == null) continue;
                Gizmos.DrawWireSphere(point.position, gizmoRadius);
                Gizmos.DrawRay(point.position, point.forward * gizmoRadius * 3f);
                Gizmos.DrawRay(point.position, point.up * gizmoRadius * 1.5f);
            }
        }
    }
}
