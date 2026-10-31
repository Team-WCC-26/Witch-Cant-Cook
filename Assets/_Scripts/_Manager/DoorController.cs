using Protocol;
using Server;
using System;
using UnityEngine;

public class DoorController : Singleton<DoorController>
{
    // 열어야 하는 문 정보
    [SerializeField] private Door lobbyDoor;
    [SerializeField] private Door kitchenDoor;

    #region Stage Actions
    public static event Action<DoorId> DoorOpened;
    #endregion

    void Start()
    {
        ServerManager.Instance.RegisterHandler(
            PacketId.S_OpenDoor,
            data => OnOpenDoor(data));


        // 문 관리
        lobbyDoor = GameObject.Find("LobbyDoor").GetComponent<Door>();
        kitchenDoor = GameObject.Find("KitchenDoor").GetComponent<Door>();

        lobbyDoor.OpenImmediate();
        kitchenDoor.CloseImmediate();
    }


    private void OnDestroy()
    {
        //ServerManager.Instance.UnRegisterHandler(PacketId.S_OpenDoor);
    }

    private void GameClear()
    {
        PlayEndingScene();
    }

    private void GameOver()
    {
    }

    private void PlayEndingScene()
    {
        // 엔딩 연출 호출
    }

    private void OnOpenDoor(ReadOnlyMemory<byte> data)
    {
        OpenDoorPacket packet = PacketSerializer.Deserialize<OpenDoorPacket>(data);

        switch (packet.DoorId)
        {
            case DoorId.Lobby:
                if (kitchenDoor != null)
                {
                    kitchenDoor.gameObject.SetActive(true);
                    kitchenDoor.Close();
                }
                if (lobbyDoor != null) lobbyDoor.Open();
                break;

            case DoorId.Kitchen:
                lobbyDoor.Close();
                kitchenDoor.Open();
                break;
        }
        DoorOpened?.Invoke(packet.DoorId);
    }
}
