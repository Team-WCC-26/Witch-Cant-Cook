using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class MapObjNetworkRouter : MonoBehaviour
{
    [SerializeField] private List<MapObjInteraction> mapInteractions = new();

    private readonly Queue<MapObjInteraction> registerQueue = new();
    private readonly Dictionary<long, MapObjInteraction> mapObjects = new();

    private MapObjInteraction currentRegisterTarget;
    private Coroutine subscribeRoutine;
    private bool isSubscribed;

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
        isSubscribed = false;
    }

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
        // Registration queue
        registerQueue.Clear();
        mapObjects.Clear();
        currentRegisterTarget = null;

        foreach (var mapObj in mapInteractions)
        {
            if (mapObj == null) continue;
            mapObj.InitializeRouter(this);
            if (mapObj.IsRegistered) continue;

            registerQueue.Enqueue(mapObj);
        }

        RegisterNext();
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
        // Server identity
        ToolRegisterPacket packet = PacketSerializer.Deserialize<ToolRegisterPacket>(data.Span);

        if (currentRegisterTarget == null)
            return;

        currentRegisterTarget.SetNetworkId(packet.EntityId);
        mapObjects[packet.EntityId] = currentRegisterTarget;

        RegisterNext();
    }
    #endregion

}
