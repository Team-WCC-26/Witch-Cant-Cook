using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IObjCombineReceiver
{
    void HandleIngredientCombine(
        IngredientCombinePacket packet,
        CatchableObj result,
        CatchableObj secondaryVisual);
}

public interface ICookReceiver
{
    void HandleCookStart(CookStartPacket packet);
    void HandleCookPause(CookPausePacket packet);
    void HandleCookComplete(CookCompletePacket packet);
}

public sealed class CookNetworkRouter : MonoBehaviour
{
    [SerializeField] private MapObjNetworkRouter mapObjRouter;
    [SerializeField] private ObjectNetworkRouter objectRouter;

    private Coroutine subscribeRoutine;
    private bool isSubscribed;

    private void OnEnable()
    {
        subscribeRoutine = StartCoroutine(SubscribeWhenReady());
    }

    private IEnumerator SubscribeWhenReady()
    {
        // Cook packet subscription
        yield return new WaitUntil(() => ServerManager.Instance != null);

        if (mapObjRouter == null)
            mapObjRouter = FindFirstObjectByType<MapObjNetworkRouter>();
        if (objectRouter == null)
            objectRouter = ObjectNetworkRouter.Instance;

        ServerManager.Instance.RegisterHandler(PacketId.S_IngredientCombine, HandleIngredientCombine);
        ServerManager.Instance.RegisterHandler(PacketId.S_CookStart, HandleCookStart);
        ServerManager.Instance.RegisterHandler(PacketId.S_CookPause, HandleCookPause);
        ServerManager.Instance.Router.OnCookCompleted += HandleCookCompleted;
        isSubscribed = true;
        subscribeRoutine = null;
    }

    private void OnDisable()
    {
        if (subscribeRoutine != null)
        {
            StopCoroutine(subscribeRoutine);
            subscribeRoutine = null;
        }

        if (!isSubscribed || ServerManager.Instance == null) return;

        ServerManager.Instance.UnRegisterHandler(PacketId.S_IngredientCombine);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_CookStart);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_CookPause);
        ServerManager.Instance.Router.OnCookCompleted -= HandleCookCompleted;
        isSubscribed = false;
    }

    private void HandleIngredientCombine(ReadOnlyMemory<byte> data)
    {
        // Container before rekey
        IngredientCombinePacket packet = MemoryPackSerializer.Deserialize<IngredientCombinePacket>(data.Span);

        if (!TryResolveSubjectContainer(packet.SubjectEntityId, out IObjCombineReceiver receiver))
        {
            Debug.LogError($"Combine container not found. SubjectEntityId: {packet.SubjectEntityId}");
            return;
        }

        if (!objectRouter.TryApplyIngredientCombine(packet, out CatchableObj result, out CatchableObj secondaryVisual))
        {
            Debug.LogError($"Combine entities not found. SubjectEntityId: {packet.SubjectEntityId}");
            return;
        }

        receiver.HandleIngredientCombine(packet, result, secondaryVisual);
    }

    private void HandleCookStart(ReadOnlyMemory<byte> data)
    {
        CookStartPacket packet = MemoryPackSerializer.Deserialize<CookStartPacket>(data.Span);

        if (!TryResolveReceiver(packet.ToolEntityId, out ICookReceiver receiver))
        {
            Debug.LogError($"Cook start target not found. ToolEntityId: {packet.ToolEntityId}");
            return;
        }

        receiver.HandleCookStart(packet);
    }

    private void HandleCookPause(ReadOnlyMemory<byte> data)
    {
        CookPausePacket packet = MemoryPackSerializer.Deserialize<CookPausePacket>(data.Span);

        if (!TryResolveReceiver(packet.ToolEntityId, out ICookReceiver receiver))
        {
            Debug.LogError($"Cook pause target not found. ToolEntityId: {packet.ToolEntityId}");
            return;
        }

        receiver.HandleCookPause(packet);
    }

    private void HandleCookCompleted(IReadOnlyList<CookCompletePacket> packets)
    {
        foreach (CookCompletePacket packet in packets)
        {
            if (!TryResolveReceiver(packet.ToolEntityId, out ICookReceiver receiver))
            {
                Debug.LogError($"Cook complete target not found. ToolEntityId: {packet.ToolEntityId}");
                continue;
            }

            receiver.HandleCookComplete(packet);
        }
    }

    private bool TryResolveSubjectContainer(long subjectEntityId, out IObjCombineReceiver receiver)
    {
        // Logical parent
        receiver = null;

        if (objectRouter == null || !objectRouter.TryGet(subjectEntityId, out CatchableObj subject))
            return false;

        if (!TryResolveContainer(subject.ParentEntityId, out MonoBehaviour container))
            return false;

        receiver = container.GetComponent<IObjCombineReceiver>();
        return receiver != null;
    }

    private bool TryResolveReceiver(long entityId, out ICookReceiver receiver)
    {
        receiver = null;

        if (!TryResolveContainer(entityId, out MonoBehaviour entity))
            return false;

        receiver = entity.GetComponent<ICookReceiver>();
        return receiver != null;
    }

    private bool TryResolveContainer(long entityId, out MonoBehaviour entity)
    {
        entity = null;

        if (mapObjRouter != null && mapObjRouter.TryGetMapObject(entityId, out MapObjInteraction mapObj))
        {
            entity = mapObj;
            return true;
        }

        if (objectRouter != null && objectRouter.TryGet(entityId, out CatchableObj catchable))
        {
            entity = catchable;
            return true;
        }

        return false;
    }
}
