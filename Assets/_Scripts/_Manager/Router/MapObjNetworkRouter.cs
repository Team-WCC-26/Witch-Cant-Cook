using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

public sealed class MapObjNetworkRouter : MonoBehaviour
{
    private const float PositionMatchTolerance = 0.01f;

    [SerializeField] private List<MapObjInteraction> mapInteractions = new();

    private readonly Queue<MapObjInteraction> registerQueue = new();
    private readonly List<MapObjInteraction> registerTargets = new();
    private readonly Dictionary<long, MapObjInteraction> mapObjects = new();

    private MapObjInteraction currentRegisterTarget;
    private Coroutine subscribeRoutine;
    private bool isSubscribed;
    private bool isRegisterOwner;

    #region unity cycle

    private void OnEnable()
    {
        StageManager.DoorOpened += OnDoorOpened;
        subscribeRoutine = StartCoroutine(SubscribeWhenReady());
    }

    private IEnumerator SubscribeWhenReady()
    {
        // Delayed subscription
        yield return new WaitUntil(() => ServerManager.Instance != null);
        ServerManager.Instance.RegisterHandler(PacketId.S_ToolRegister, OnToolRegistered);
        ServerManager.Instance.RegisterHandler(PacketId.S_ToolPlayerEnter, OnPotPlayerEntered);
        ServerManager.Instance.RegisterHandler(PacketId.S_PotEject, OnPotEjected);
        ServerManager.Instance.RegisterHandler(PacketId.S_PotCookComplete, OnPotCookCompleted);
        isSubscribed = true;
        subscribeRoutine = null;
    }

    private void OnDisable()
    {
        StageManager.DoorOpened -= OnDoorOpened;

        if (subscribeRoutine != null)
        {
            StopCoroutine(subscribeRoutine);
            subscribeRoutine = null;
        }

        if (!isSubscribed || ServerManager.Instance == null) return;

        ServerManager.Instance.UnRegisterHandler(PacketId.S_ToolRegister);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_ToolPlayerEnter);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_PotEject);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_PotCookComplete);
        isSubscribed = false;
    }

    #endregion

    #region Map Object Getters
    public bool TryGetMapObject(long entityId, out MapObjInteraction mapObj)
    {
        return mapObjects.TryGetValue(entityId, out mapObj);
    }

    public bool TryGetMapObject<T>(long entityId, out T obj) where T : MapObjInteraction
    {
        obj = null;

        if (!mapObjects.TryGetValue(entityId, out MapObjInteraction mapObj))
            return false;

        obj = mapObj as T;
        return obj != null;
    }
    #endregion

    #region Map Object Register
    private void OnDoorOpened(DoorId door)
    {
        if (door == DoorId.Kitchen)
            BeginRegister();
    }

    private void BeginRegister()
    {
        registerQueue.Clear();
        registerTargets.Clear();
        mapObjects.Clear();
        currentRegisterTarget = null;
        isRegisterOwner = IsRegisterOwner();

        HashSet<MapObjInteraction> targets = new();

        foreach (MapObjInteraction mapObj in mapInteractions)
        {
            if (mapObj != null)
                targets.Add(mapObj);
        }

        foreach (MapObjInteraction mapObj in FindObjectsByType<MapObjInteraction>(FindObjectsSortMode.None))
            targets.Add(mapObj);

        foreach (MapObjInteraction mapObj in targets)
        {
            mapObj.InitializeRouter(this);
            if (mapObj.IsRegistered) continue;

            registerTargets.Add(mapObj);

            if (isRegisterOwner)
                registerQueue.Enqueue(mapObj);
        }

        if (isRegisterOwner)
            RegisterNext();
    }

    private bool IsRegisterOwner()
    {
        if (PlayerSpawnManager.Instance == null)
            return false;

        string ownerId = null;

        foreach (PlayerBrain player in PlayerSpawnManager.Instance.Players)
        {
            if (player == null || string.IsNullOrEmpty(player.PlayerId)) continue;

            if (ownerId == null || string.CompareOrdinal(player.PlayerId, ownerId) < 0)
                ownerId = player.PlayerId;
        }

        return ownerId != null && ownerId == PlayerSpawnManager.Instance.MyID;
    }

    private void RegisterNext()
    {
        if (registerQueue.Count == 0)
        {
            currentRegisterTarget = null;
            return;
        }

        currentRegisterTarget = registerQueue.Dequeue();

        ToolRegisterPacket packet = new()
        {
            EntityId = 0,
            ToolId = currentRegisterTarget.ToolId,
            Position = ProtocolTypeConverter.ToNumericsVector3(currentRegisterTarget.transform.position),
            Quaternion = ProtocolTypeConverter.ToNumericsQuaternion(currentRegisterTarget.transform.rotation)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void OnToolRegistered(ReadOnlyMemory<byte> data)
    {
        ToolRegisterPacket packet = PacketSerializer.Deserialize<ToolRegisterPacket>(data.Span);
        MapObjInteraction target = FindRegisterTarget(packet);

        if (target == null)
        {
            Debug.LogWarning(
                $"Map object registration target not found. ToolId: {packet.ToolId}, Position: {packet.Position}");
            return;
        }

        target.SetNetworkId(packet.EntityId);
        mapObjects[packet.EntityId] = target;

        if (!isRegisterOwner || currentRegisterTarget != target) return;

        currentRegisterTarget = null;
        RegisterNext();
    }

    private MapObjInteraction FindRegisterTarget(ToolRegisterPacket packet)
    {
        Vector3 packetPosition = ProtocolTypeConverter.ToUnityVector3(packet.Position);
        float toleranceSqr = PositionMatchTolerance * PositionMatchTolerance;

        foreach (MapObjInteraction target in registerTargets)
        {
            if (target == null || target.IsRegistered) continue;
            if (target.ToolId != packet.ToolId) continue;
            if ((target.transform.position - packetPosition).sqrMagnitude > toleranceSqr) continue;

            return target;
        }

        return null;
    }
    #endregion

    #region Pot Packet Routing

    // 솥 플레이어 진입 패킷 전달
    private void OnPotPlayerEntered(ReadOnlyMemory<byte> data)
    {
        ToolPlayerEnterPacket packet = MemoryPackSerializer.Deserialize<ToolPlayerEnterPacket>(data.Span);
        if (TryGetPotReceiver(packet.ToolEntityId, out IPotNetworkReceiver receiver))
            receiver.HandlePotPlayerEnter(packet);
    }

    // 솥 배출 패킷 전달
    private void OnPotEjected(ReadOnlyMemory<byte> data)
    {
        PotEjectPacket packet = MemoryPackSerializer.Deserialize<PotEjectPacket>(data.Span);
        if (TryGetPotReceiver(packet.PotEntityId, out IPotNetworkReceiver receiver))
            receiver.HandlePotEject(packet);
    }

    // 솥 조리 완료 패킷 전달
    private void OnPotCookCompleted(ReadOnlyMemory<byte> data)
    {
        PotCookCompletePacket packet = MemoryPackSerializer.Deserialize<PotCookCompletePacket>(data.Span);
        if (TryGetPotReceiver(packet.PotEntityId, out IPotNetworkReceiver receiver))
            receiver.HandlePotCookComplete(packet);
    }

    // 솥 네트워크 수신 대상 조회
    private bool TryGetPotReceiver(long potEntityId, out IPotNetworkReceiver receiver)
    {
        receiver = null;
        if (!TryGetMapObject(potEntityId, out PotInteraction pot)) return false;

        receiver = pot;
        return true;
    }

    #endregion

}