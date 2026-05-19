using Cysharp.Threading.Tasks;
using Protocol;
using Server;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class GlobalSpawnManager : MonoBehaviour
{
    private readonly int[] ingredientIDs = { 10900, 10300, 11900, 12000, 10600 };

    private void Update()
    {
        // F1 키: 서버에 재료 스폰 요청 생성 패킷 송신
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            int randomID = ingredientIDs[UnityEngine.Random.Range(0, ingredientIDs.Length)];
            SendSpawnPacketToServer(randomID);
        }

        // ESC 키: 마우스 커서 락 해제
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void SendSpawnPacketToServer(int ingredientID)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        float3 targetPosition = GetSpawnPosition();

        var spawnPacket = new IngredientSpawnPacket
        {
            EntityId = 0, // 클라이언트의 생성 요청 0 
            IngredientID = ingredientID,
            Position = new System.Numerics.Vector3(targetPosition.x, targetPosition.y, targetPosition.z),
            Quaternion = System.Numerics.Quaternion.Identity
        };

        byte[] sendBuffer = PacketSerializer.Serialize(spawnPacket);

        if (ServerManager.Instance != null)
        {
            // 비동기로 서버에 패킷 전송
            ServerManager.Instance.SendData(sendBuffer).Forget();
            Debug.Log($"[Network Send] 서버로 스폰 요청 전송 완료 ID: {ingredientID}, 좌표: {targetPosition}");
        }
        else
        {
            Debug.LogError("[Network Error] ServerManager.Instance를 찾을 수 없습니다.");
        }
    }

    private float3 GetSpawnPosition()
    {
        GameObject spawnPointObj = GameObject.Find("SpawnPoint");
        return spawnPointObj != null ? (float3)spawnPointObj.transform.position : float3.zero;
    }
}