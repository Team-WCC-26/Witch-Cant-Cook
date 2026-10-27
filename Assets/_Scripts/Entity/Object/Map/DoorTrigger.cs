using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;

public class DoorTrigger : MonoBehaviour
{
    [SerializeField] private DoorId doorId = DoorId.Kitchen;

    // The server counts players per door, not colliders or individual pressure plates.
    private static readonly Dictionary<(DoorId Door, string Player), HashSet<DoorTrigger>> occupiedTriggers = new();
    private readonly HashSet<Collider> localContacts = new();
    private string localPlayerId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOccupancy() => occupiedTriggers.Clear();

    private void OnTriggerEnter(Collider other) => TrackContact(other);
    private void OnTriggerStay(Collider other) => TrackContact(other);

    private void TrackContact(Collider other)
    {
        if (other == null || PlayerSpawnManager.Instance == null ||
            ServerManager.Instance == null || !ServerManager.Instance.IsConnected) return;
        // Body colliders can live below PlayerBrain and do not all carry the Player tag.
        PlayerBrain player = other.GetComponentInParent<PlayerBrain>();
        if (player == null || !PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        // Held props must not count as the player standing on the plate.
        if (other.GetComponentInParent<CatchableObj>() != null) return;

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
        // Remote players and individual child-collider exits cannot cancel our presence.
        if (!localContacts.Remove(other) || localContacts.Count > 0) return;
        ReleasePresence();
    }

    private void FixedUpdate()
    {
        if (localPlayerId == null) return;
        localContacts.RemoveWhere(col => col == null || !col.enabled || !col.gameObject.activeInHierarchy);
        if (localContacts.Count == 0 || PlayerSpawnManager.Instance == null ||
            !PlayerSpawnManager.Instance.IsMine(localPlayerId) ||
            ServerManager.Instance == null || !ServerManager.Instance.IsConnected)
            ReleasePresence();
    }

    private void OnDisable() => ReleasePresence();

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
