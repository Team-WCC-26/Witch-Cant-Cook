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

        InteractDoorPacket packet = new()
        {
            DoorId = DoorId.Kitchen,
            PlayerId = PlayerSpawnManager.Instance.MyID
        };
        var data = PacketSerializer.Serialize(packet);
        _ = ServerManager.Instance.SendData(data);
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
