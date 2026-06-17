using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class MapObjNetworkRouter : MonoBehaviour
{
    [SerializeField] private List<PrepInteraction> prepInteractions = new();

    private readonly Queue<PrepInteraction> registerQueue = new();
    private readonly Dictionary<long, MapObjInteraction> mapObjects = new();

    private PrepInteraction currentRegisterTarget;

    private IEnumerator Start()
    {
        yield return new WaitUntil(() => ServerManager.Instance != null);

        ServerManager.Instance.RegisterHandler(PacketId.S_ToolRegister, OnToolRegistered);
        ServerManager.Instance.RegisterHandler(PacketId.S_IngredientPut, OnIngredientPut);
    }

    private void OnEnable()
    {
        StageManager.DoorOpened += OnDoorOpened;
    }

    private void OnDisable()
    {
        StageManager.DoorOpened -= OnDoorOpened;

        if (ServerManager.Instance == null) return;

        ServerManager.Instance.UnRegisterHandler(PacketId.S_ToolRegister);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_IngredientPut);
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
        registerQueue.Clear();
        mapObjects.Clear();
        currentRegisterTarget = null;

        foreach (PrepInteraction prep in prepInteractions)
        {
            if (prep == null) continue;
            prep.InitializeRouter(this);
            if (prep.IsRegistered) continue;

            registerQueue.Enqueue(prep);
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
        ToolRegisterPacket packet = PacketSerializer.Deserialize<ToolRegisterPacket>(data.Span);

        if (currentRegisterTarget == null)
            return;

        currentRegisterTarget.SetNetworkId(packet.EntityId);
        mapObjects[packet.EntityId] = currentRegisterTarget;

        RegisterNext();
    }
    #endregion

    #region Prep Interaction - Ingredient Put
    public void RequestPut(PrepInteraction prep, CatchableObj catchable)
    {
        if (prep == null) return;
        if (catchable == null) return;
        if (!prep.IsRegistered) return;
        if (ServerManager.Instance == null) return;

        IngredientPutPacket packet = new()
        {
            IngredientId = catchable.NetworkId,
            CountertopId = prep.NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void OnIngredientPut(ReadOnlyMemory<byte> data)
    {
        IngredientPutPacket packet = PacketSerializer.Deserialize<IngredientPutPacket>(data.Span);

        if (!TryGetMapObject(packet.CountertopId, out PrepInteraction prep))
            return;

        if (!ObjectNetworkRouter.Instance.TryGet(packet.IngredientId, out CatchableObj catchable))
            return;

        prep.ApplyPut(catchable);
    }
    #endregion
}
