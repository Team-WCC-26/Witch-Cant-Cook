using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerNetworkRouter : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(0.3f);
        ServerManager.Instance.Router.OnPlayer += RoutePlayerState;
        ServerManager.Instance.RegisterHandler(PacketId.S_EntityPickup, RouteEntityPickup);
    }

    private void OnEnable()
    {
        //Debug.Log("PlayerNetworkRouter Enabled");
        //ServerManager.Instance.Router.OnPlayer += RoutePlayerState;
        //ServerManager.Instance.RegisterHandler(PacketId.S_EntityPickup, RouteEntityPickup);
    }

    private void OnDisable()
    {
        if (ServerManager.Instance == null) return;

        ServerManager.Instance.Router.OnPlayer -= RoutePlayerState;
        ServerManager.Instance.UnRegisterHandler(PacketId.S_EntityPickup);
    }

    private void RoutePlayerState(IReadOnlyList<PlayerMovementPacket> list)
    {
        if (PlayerSpawnManager.Instance == null) return;

        foreach (PlayerBrain player in PlayerSpawnManager.Instance.Players)
        {
            player.StateResolver.ApplyRemotePacket(list);
        }
    }

    private void RouteEntityPickup(ReadOnlyMemory<byte> data)
    {
        Debug.Log("a");

        EntityPickupPacket packet =
            PacketSerializer.Deserialize<EntityPickupPacket>(data.Span);

        if (PlayerSpawnManager.Instance == null) return;

        if (!PlayerSpawnManager.Instance.TryGetPlayer(packet.PlayerID, out PlayerBrain player))
        {
            Debug.Log("Player 부재");
            return;
        }

        if (!ObjectRouter.Instance.TryGet(packet.EntityId, out CatchableObj target))
        {
            Debug.Log("Catch Target 부재");
            return;
        }

        player.Interact.ApplyPicked(target);
    }
}
