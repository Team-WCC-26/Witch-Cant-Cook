using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public sealed class PlayerSpawnManager : Singleton<PlayerSpawnManager>
{
    [Header("Local Player")]
    [SerializeField] private string myID = null;

    [Header("Spawn")]
    [SerializeField] private PlayerBrain playerPrefab = null;
    [SerializeField] private Transform spawnRoot = null;

    [Header("Camera")]
    [SerializeField] private Camera mainCamera = null;
    [SerializeField] private CinemachineCamera virtualCamera = null;

    private readonly Dictionary<string, PlayerBrain> players = new();
    private Coroutine subscribeRoutine;
    private bool isSubscribed;

    //Packet Ids
    private PacketId _joinMemberID => PacketId.S_PlayerEnter;
    private PacketId _leaveMemberID => PacketId.S_PlayerLeave;

    #region Unity Callbacks

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        subscribeRoutine = StartCoroutine(WaitForServerManager());
    }

    private void OnDisable()
    {
        if (subscribeRoutine != null) StopCoroutine(subscribeRoutine);
        subscribeRoutine = null;
        if (!isSubscribed || ServerManager.Instance == null) return;
        ServerManager.Instance.UnRegisterHandler(_joinMemberID);
        ServerManager.Instance.UnRegisterHandler(_leaveMemberID);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_PlayerRespawn);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_IngredientSquid);
        isSubscribed = false;
    }

    #endregion

    public string MyID
    {
        get => myID;
        set => myID = value;
    }

    public IEnumerable<PlayerBrain> Players => players.Values;
    public Transform InitialSpawnPoint => spawnRoot;

    // Use the same initial spawn anchor on every client; keep the two players apart.
    public bool TryGetLobbyReturnSpawns(IReadOnlyList<string> participants,
        out Dictionary<string, Pose> poses, out string issue)
    {
        poses = new Dictionary<string, Pose>();
        issue = null;
        if (spawnRoot == null)
        {
            issue = "PlayerSpawnManager의 초기 스폰 위치(spawnRoot)를 지정하세요.";
            return false;
        }
        if (participants == null || participants.Count == 0 || participants.Count > 2)
        {
            issue = "로비 참가자 정보 대기 중 (1~2명)";
            return false;
        }
        var ids = new HashSet<string>();
        foreach (string id in participants)
        {
            if (string.IsNullOrEmpty(id) || !ids.Add(id))
            {
                issue = "로비 참가자 ID가 없거나 중복되었습니다.";
                return false;
            }
        }
        for (int i = 0; i < participants.Count; i++)
        {
            Vector3 position = spawnRoot.TransformPoint(new Vector3((i - 0.5f) * 1.5f, 0f, 0f));
            poses.Add(participants[i], new Pose(position, spawnRoot.rotation));
        }
        return true;
    }

    public bool IsMine(string playerId)
    {
        return !string.IsNullOrEmpty(playerId) && playerId == myID;
    }

    public PlayerBrain SpawnPlayer(string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return null;

        if (players.TryGetValue(playerId, out PlayerBrain existing))
            return existing;

        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        PlayerBrain player = Instantiate(playerPrefab, spawnRoot.position, spawnRoot.rotation, spawnRoot);
        player.Initialize(playerId);
        if (IsMine(playerId)) BindCamera(player); 

        return player;
    }

    public void RegisterPlayer(PlayerBrain player)
    {
        if (player == null || string.IsNullOrEmpty(player.PlayerId))
        {
            return;
        }

        players[player.PlayerId] = player;
    }

    public void UnregisterPlayer(PlayerBrain player)
    {
        if (player == null || string.IsNullOrEmpty(player.PlayerId))
        {
            return;
        }

        if (players.TryGetValue(player.PlayerId, out PlayerBrain registeredPlayer) &&
            registeredPlayer == player)
        {
            players.Remove(player.PlayerId);
        }
    }

    public bool TryGetPlayer(string playerId, out PlayerBrain player)
    {
        return players.TryGetValue(playerId, out player);
    }

    public bool ContainsPlayer(string playerId)
    {
        return players.ContainsKey(playerId);
    }

    private void BindCamera(PlayerBrain player)
    {
        player.BindCamera(mainCamera, virtualCamera);
    }

    private void MemberJoined(ReadOnlyMemory<byte> data)
    {
        var packet = MemoryPackSerializer.Deserialize<PlayerEnterPacket>(data.Span);
        string playerId = packet.NewPlayerID;

        if (!ContainsPlayer(playerId))
            SpawnPlayer(playerId);

        StageSelectionSession.Instance.OnPlayerEntered(playerId);
        RoomHudSession.Instance.OnPlayerEntered(playerId);

        UIManager.Hide<LobbyRouterUI>();
    }

    private void MemberLeft(ReadOnlyMemory<byte> data)
    {
        PlayerLeavePacket packet = MemoryPackSerializer.Deserialize<PlayerLeavePacket>(data.Span);
        RoomHudSession.Instance.OnPlayerLeft(packet.PlayerID);

        if (!players.TryGetValue(packet.PlayerID, out PlayerBrain player)) return;
        Destroy(player.gameObject);
    }

    public bool RespawnPlayer(string playerId, Transform spawnPoint = null)
    {
        if (!players.TryGetValue(playerId, out PlayerBrain player))
            return false;

        Transform point = spawnPoint != null ? spawnPoint : spawnRoot;
        if (point == null) return false;

        player.Rocket?.StopFlight();
        player.Health.Reset();
        if (player.StateResolver is RemotePlayerStateResolver remote)
            remote.ResetForStageReturn(new Pose(point.position, point.rotation));
        player.ActionController.Movement.UpdateEjectionMovement(false);
        player.ActionController.Movement.BeginForcedMovement(0f);
        if (player.Interact.HeldObj is CatchableObj held && player.Interact.TryReleaseHeld(held)) held.OnDrop();
        Vector3 previousPos = player.transform.position;
        Quaternion rotationDelta = point.rotation * Quaternion.Inverse(player.transform.rotation);
        var bodyPoses = new List<(Rigidbody body, Vector3 pos, Quaternion rot)>();
        foreach (BodyPart part in player.BodyParts)
        {
            if (part == null || part.Rb == null || part.Rb.isKinematic) continue;
            bodyPoses.Add((part.Rb, point.position + rotationDelta * (part.Rb.position - previousPos),
                rotationDelta * part.Rb.rotation));
        }
        if (player.Rb != null)
        {
            if (!player.Rb.isKinematic)
            {
                player.Rb.linearVelocity = Vector3.zero;
                player.Rb.angularVelocity = Vector3.zero;
            }
            player.Rb.position = point.position;
            player.Rb.rotation = point.rotation;
        }

        player.transform.SetPositionAndRotation(
            point.position,
            point.rotation);
        foreach (var pose in bodyPoses)
        {
            pose.body.position = pose.pos;
            pose.body.rotation = pose.rot;
            pose.body.linearVelocity = Vector3.zero;
            pose.body.angularVelocity = Vector3.zero;
        }

        return true;
    }

    private void MemberRespawned(ReadOnlyMemory<byte> data)
    {
        PlayerRespawnPacket packet = PacketSerializer.Deserialize<PlayerRespawnPacket>(data.Span);
        RespawnPlayer(packet.PlayerID);
    }

    private void ReceiveSquidInk(ReadOnlyMemory<byte> data)
    {
        IngredientSquidPacket packet = PacketSerializer.Deserialize<IngredientSquidPacket>(data.Span);
        if (packet.PlayerIds == null || packet.BlindDurationMs <= 0) return;
        float duration = packet.BlindDurationMs / 1000f;
        foreach (string playerId in packet.PlayerIds)
        {
            if (string.IsNullOrEmpty(playerId) || !players.TryGetValue(playerId, out PlayerBrain player) || player == null)
                continue;
            player.EffectController.ApplyBlind(duration);
        }
    }

    private IEnumerator WaitForServerManager()
    {
        yield return new WaitUntil(() => ServerManager.Instance != null);

        ServerManager.Instance.RegisterHandler(_joinMemberID, MemberJoined);
        ServerManager.Instance.RegisterHandler(_leaveMemberID, MemberLeft);
        ServerManager.Instance.RegisterHandler(PacketId.S_PlayerRespawn, MemberRespawned);
        ServerManager.Instance.RegisterHandler(PacketId.S_IngredientSquid, ReceiveSquidInk);
        isSubscribed = true;
        subscribeRoutine = null;
    }

}
