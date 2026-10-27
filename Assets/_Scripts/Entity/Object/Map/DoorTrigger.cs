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

        SendDoorPacket(PlayerSpawnManager.Instance.MyID);        
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

    public void SendDoorPacket(string id)
    {
        InteractDoorPacket packet = new()
        {
            DoorId = DoorId.Kitchen,
            PlayerId = id
        };
        var data = PacketSerializer.Serialize(packet);
        _ = ServerManager.Instance.SendData(data);

    }
}
