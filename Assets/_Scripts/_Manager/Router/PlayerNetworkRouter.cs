using Protocol;
using Server;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerNetworkRouter : MonoBehaviour
{
    [SerializeField] private ObjectNetworkRouter objectRouter;

    private Coroutine subscribeRoutine;
    private bool isSubscribed;

    private void Awake()
    {
        if (objectRouter == null)
            objectRouter = GetComponent<ObjectNetworkRouter>();
    }

    private void OnEnable()
    {
        subscribeRoutine = StartCoroutine(SubscribeWhenReady());
    }

    private void OnDisable()
    {
        if (subscribeRoutine != null)
        {
            StopCoroutine(subscribeRoutine);
            subscribeRoutine = null;
        }
        if (!isSubscribed || ServerManager.Instance == null) return;

        ServerManager.Instance.Router.OnPlayerMoved -= RoutePlayerState;
        ServerManager.Instance.Router.OnEntityPickup -= RouteEntityPickup;
        isSubscribed = false;
    }

    private IEnumerator SubscribeWhenReady()
    {
        // World state subscription
        yield return new WaitUntil(() => ServerManager.Instance != null);

        ServerManager.Instance.Router.OnPlayerMoved += RoutePlayerState;
        ServerManager.Instance.Router.OnEntityPickup += RouteEntityPickup;
        isSubscribed = true;
        subscribeRoutine = null;
    }

    private void RoutePlayerState(IReadOnlyList<PlayerMovementPacket> packets)
    {
        // Movement fan-out
        if (PlayerSpawnManager.Instance == null) return;

        foreach (PlayerMovementPacket packet in packets)
        {
            if (PlayerSpawnManager.Instance.IsMine(packet.PlayerId))
                continue;

            if (!PlayerSpawnManager.Instance.TryGetPlayer(packet.PlayerId, out PlayerBrain player))
            {
                Debug.LogWarning($"Player not found. PlayerID: {packet.PlayerId}");
                continue;
            }

            player.StateResolver.ApplyRemotePacket(packet);
        }
    }

    private void RouteEntityPickup(IReadOnlyList<EntityPickupPacket> packets)
    {
        // Pickup authority
        if (PlayerSpawnManager.Instance == null) return;

        foreach (EntityPickupPacket packet in packets)
        {
            if (!PlayerSpawnManager.Instance.TryGetPlayer(packet.PlayerID, out PlayerBrain player))
            {
                Debug.LogError($"Player not found. PlayerID: {packet.PlayerID}");
                continue;
            }

            if (objectRouter == null || !objectRouter.TryGet(packet.EntityId, out CatchableObj target))
            {
                Debug.LogError($"Pickup target not found. EntityId: {packet.EntityId}");
                continue;
            }

            GameEvents.OnEntityPicked?.Invoke(new EntityPickedEvent(packet.EntityId));
            objectRouter.HandleEntityPicked(target);
            player.Interact.ApplyPicked(target);
        }
    }
}
