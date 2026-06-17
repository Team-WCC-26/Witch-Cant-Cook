using Protocol;
using Server;
using UnityEngine;

public class DoorTrigger : MonoBehaviour
{
    [SerializeField]
    private DoorId doorId = DoorId.Kitchen;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        PlayerBrain player = other.GetComponent<PlayerBrain>();

        if (player == null || player.PlayerId != PlayerSpawnManager.Instance.MyID)
            return;

        InteractDoorPacket packet = new()
        {
            DoorId = DoorId.Kitchen,
            PlayerId = PlayerSpawnManager.Instance.MyID
        };
        var data = PacketSerializer.Serialize(packet);
        _ = ServerManager.Instance.SendData(data);
        
        Debug.Log($"DoorTrigger: Player entered trigger for door {PlayerSpawnManager.Instance.MyID} ismine? {PlayerSpawnManager.Instance.IsMine(PlayerSpawnManager.Instance.MyID)}");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        StopInteractDoorPacket packet = new()
        {
            DoorId = doorId
        };

        var data = PacketSerializer.Serialize(packet);
        _ = ServerManager.Instance.SendData(data);
    }
}
