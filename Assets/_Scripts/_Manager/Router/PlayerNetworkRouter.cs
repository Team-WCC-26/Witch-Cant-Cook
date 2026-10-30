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
    private readonly Dictionary<string, int> pendingHealth = new();
    private readonly List<string> readyHealthIds = new();

    #region Unity Callbacks
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
        pendingHealth.Clear();
        readyHealthIds.Clear();
        if (subscribeRoutine != null)
        {
            StopCoroutine(subscribeRoutine);
            subscribeRoutine = null;
        }
        if (!isSubscribed || ServerManager.Instance == null) return;

        ServerManager.Instance.Router.OnPlayerMoved -= RoutePlayerState;
        ServerManager.Instance.Router.OnPlayerHealthChanged -= RoutePlayerHealth;
        ServerManager.Instance.Router.OnEntityPickup -= RouteEntityPickup;
        isSubscribed = false;
    }

    private void Update()
    {
        if (pendingHealth.Count == 0 || PlayerSpawnManager.Instance == null) return;
        readyHealthIds.Clear();
        foreach (var pair in pendingHealth)
        {
            if (!PlayerSpawnManager.Instance.TryGetPlayer(pair.Key, out PlayerBrain player) || player.Health == null)
                continue;
            player.Health.ApplyServerHealth(pair.Value);
            readyHealthIds.Add(pair.Key);
        }
        foreach (string playerId in readyHealthIds) pendingHealth.Remove(playerId);
    }

    #endregion

    private IEnumerator SubscribeWhenReady()
    {
        // World state subscription
        yield return new WaitUntil(() => ServerManager.Instance != null);

        ServerManager.Instance.Router.OnPlayerMoved += RoutePlayerState;
        ServerManager.Instance.Router.OnPlayerHealthChanged += RoutePlayerHealth;
        ServerManager.Instance.Router.OnEntityPickup += RouteEntityPickup;
        isSubscribed = true;
        subscribeRoutine = null;
    }

    private void RoutePlayerHealth(IReadOnlyList<PlayerHpPacket> packets)
    {
        if (packets == null) return;
        foreach (PlayerHpPacket packet in packets)
        {
            if (string.IsNullOrEmpty(packet.PlayerId)) continue;
            if (PlayerSpawnManager.Instance != null &&
                PlayerSpawnManager.Instance.TryGetPlayer(packet.PlayerId, out PlayerBrain player) && player.Health != null)
            {
                pendingHealth.Remove(packet.PlayerId);
                player.Health.ApplyServerHealth(packet.Hp);
            }
            else
            {
                // Preserve HP received before the player prefab finishes spawning.
                pendingHealth[packet.PlayerId] = packet.Hp;
            }
        }
    }

    private void RoutePlayerState(IReadOnlyList<PlayerMovementPacket> packets)
    {
        if (StageReturnFlow.IsInputBlocked) return;
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
        if (StageReturnFlow.IsInputBlocked) return;
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
