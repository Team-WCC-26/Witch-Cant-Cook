using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;

public class DoorTrigger : MonoBehaviour
{
    [SerializeField] private DoorId doorId = DoorId.Kitchen;

    // The server counts players per door, not colliders or individual pressure plates.
    private static readonly Dictionary<(DoorId Door, string Player), HashSet<DoorTrigger>> occupiedTriggers = new();
    // Presentation observes both players; network interaction packets remain local-player only.
    private static readonly Dictionary<(DoorId Door, string Player), HashSet<DoorTrigger>> observedTriggers = new();
    private readonly Dictionary<string, HashSet<Collider>> observedContacts = new();
    private readonly List<string> staleObservedPlayers = new();
    private RoomHudSession hudSession;
    private readonly HashSet<Collider> localContacts = new();
    private string localPlayerId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOccupancy() { occupiedTriggers.Clear(); observedTriggers.Clear(); }

    private void OnTriggerEnter(Collider other) => TrackContact(other);
    private void OnTriggerStay(Collider other) => TrackContact(other);

    private void TrackContact(Collider other)
    {
        if (other == null || PlayerSpawnManager.Instance == null ||
            ServerManager.Instance == null || !ServerManager.Instance.IsConnected) return;
        // Body colliders can live below PlayerBrain and do not all carry the Player tag.
        PlayerBrain player = other.GetComponentInParent<PlayerBrain>();
        if (player == null || string.IsNullOrEmpty(player.PlayerId)) return;
        // Held props must not count as the player standing on the plate.
        if (other.GetComponentInParent<CatchableObj>() != null) return;

        TrackObservedContact(player.PlayerId, other);
        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;

        if (localPlayerId != null && localPlayerId != player.PlayerId) ReleasePresence();
        localPlayerId = player.PlayerId;
        localContacts.Add(other);

        var key = (doorId, localPlayerId);
        if (!occupiedTriggers.TryGetValue(key, out HashSet<DoorTrigger> triggers))
        {
            triggers = new HashSet<DoorTrigger>();
            occupiedTriggers.Add(key, triggers);
        }
        if (triggers.Add(this) && triggers.Count == 1) SendDoorPacket(localPlayerId);
    }

    private void OnTriggerExit(Collider other)
    {
        ReleaseObservedContact(other);
        // Remote players and individual child-collider exits cannot cancel our presence.
        if (!localContacts.Remove(other) || localContacts.Count > 0) return;
        ReleasePresence();
    }

    private void FixedUpdate()
    {
        PruneObservedContacts();
        if (localPlayerId == null) return;
        localContacts.RemoveWhere(col => col == null || !col.enabled || !col.gameObject.activeInHierarchy);
        if (localContacts.Count == 0 || PlayerSpawnManager.Instance == null ||
            !PlayerSpawnManager.Instance.IsMine(localPlayerId) ||
            ServerManager.Instance == null || !ServerManager.Instance.IsConnected)
            ReleasePresence();
    }

    private void OnDisable()
    {
        ClearObservedPresence();
        ReleasePresence();
    }

    private void TrackObservedContact(string playerId, Collider collider)
    {
        if (!observedContacts.TryGetValue(playerId, out HashSet<Collider> contacts))
        {
            contacts = new HashSet<Collider>();
            observedContacts.Add(playerId, contacts);
        }
        contacts.Add(collider);
        var key = (doorId, playerId);
        if (!observedTriggers.TryGetValue(key, out HashSet<DoorTrigger> triggers))
        {
            triggers = new HashSet<DoorTrigger>();
            observedTriggers.Add(key, triggers);
        }
        triggers.Add(this);
        // Stay also repairs presentation if room/HUD initialization finished after trigger entry.
        if (hudSession == null) hudSession = RoomHudSession.Instance;
        hudSession.SetDoorPlatePresence(doorId, playerId, true);
    }

    private void ReleaseObservedContact(Collider collider)
    {
        staleObservedPlayers.Clear();
        foreach (var pair in observedContacts)
            if (pair.Value.Remove(collider) && pair.Value.Count == 0) staleObservedPlayers.Add(pair.Key);
        foreach (string playerId in staleObservedPlayers)
        {
            observedContacts.Remove(playerId);
            ReleaseObservedPlayer(playerId);
        }
    }

    private void PruneObservedContacts()
    {
        if (observedContacts.Count == 0) return;
        if (PlayerSpawnManager.Instance == null || ServerManager.Instance == null || !ServerManager.Instance.IsConnected)
        {
            ClearObservedPresence();
            return;
        }
        staleObservedPlayers.Clear();
        foreach (var pair in observedContacts)
        {
            pair.Value.RemoveWhere(col => col == null || !col.enabled || !col.gameObject.activeInHierarchy);
            if (pair.Value.Count == 0 || !PlayerSpawnManager.Instance.ContainsPlayer(pair.Key))
                staleObservedPlayers.Add(pair.Key);
        }
        foreach (string playerId in staleObservedPlayers)
        {
            observedContacts.Remove(playerId);
            ReleaseObservedPlayer(playerId);
        }
    }

    private void ClearObservedPresence()
    {
        foreach (string playerId in observedContacts.Keys) ReleaseObservedPlayer(playerId);
        observedContacts.Clear();
    }

    private void ReleaseObservedPlayer(string playerId)
    {
        var key = (doorId, playerId);
        if (!observedTriggers.TryGetValue(key, out HashSet<DoorTrigger> triggers) ||
            !triggers.Remove(this) || triggers.Count > 0) return;
        observedTriggers.Remove(key);
        if (hudSession != null) hudSession.SetDoorPlatePresence(doorId, playerId, false);
    }

    private void ReleasePresence()
    {
        string playerId = localPlayerId;
        localContacts.Clear();
        localPlayerId = null;
        if (string.IsNullOrEmpty(playerId)) return;

        var key = (doorId, playerId);
        if (!occupiedTriggers.TryGetValue(key, out HashSet<DoorTrigger> triggers) ||
            !triggers.Remove(this) || triggers.Count > 0) return;
        occupiedTriggers.Remove(key);
        if (ServerManager.Instance == null || !ServerManager.Instance.IsConnected ||
            PlayerSpawnManager.Instance == null || !PlayerSpawnManager.Instance.IsMine(playerId)) return;

        StopInteractDoorPacket packet = new()
        {
            DoorId = doorId,
            PlayerId = playerId
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void SendDoorPacket(string id)
    {
        if (ServerManager.Instance == null || !ServerManager.Instance.IsConnected ||
            PlayerSpawnManager.Instance == null || !PlayerSpawnManager.Instance.IsMine(id)) return;
        InteractDoorPacket packet = new()
        {
            DoorId = doorId,
            PlayerId = id
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }
}
