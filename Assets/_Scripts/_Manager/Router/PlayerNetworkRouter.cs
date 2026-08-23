using Protocol;
using Server;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerNetworkRouter : MonoBehaviour
{
    private Coroutine subscribeRoutine;
    private bool isSubscribed;

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

        foreach (PlayerBrain player in PlayerSpawnManager.Instance.Players)
            player.StateResolver.ApplyRemotePacket(packets);
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

            if (!ObjectNetworkRouter.Instance.TryGet(packet.EntityId, out CatchableObj target))
            {
                Debug.LogError($"Pickup target not found. EntityId: {packet.EntityId}");
                continue;
            }

            GameEvents.OnEntityPicked?.Invoke(new EntityPickedEvent(packet.EntityId));
            player.Interact.ApplyPicked(target);
        }
    }
}
