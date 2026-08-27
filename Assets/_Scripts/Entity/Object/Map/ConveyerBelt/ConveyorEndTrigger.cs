using Cysharp.Threading.Tasks;
using MemoryPack;
using Protocol;
using Server;
using UnityEngine;

/// <summary>
/// 벨트 끝 지점에 배치하는 트리거 콜라이더 전용 스크립트.
/// ConveyorBeltController와는 별도 오브젝트(벨트 마지막 waypoint 근처)에 부착한다.
///
/// 역할:
/// 1) 재료(CatchableObj)가 이 트리거에 닿으면 EntityDestroyPacket을 서버로 전송
/// 2) ConveyorBeltController의 이동 관리 리스트에서 해당 재료를 제거 (UnregisterItem)
/// 3) 같은 재료가 콜라이더 안에 머무는 동안 중복 신고되지 않도록 가드
///
/// 주의: 실제 오브젝트 파괴/풀 반환은 서버 응답을 받은 뒤 처리하는 것을 권장.
/// (서버가 "알아서 오브젝트 풀에 넣어준다"고 했으므로, 여기서는 이동만 멈추고
///  파괴 자체는 서버가 보내는 후속 이벤트/응답에서 처리하는 편이 안전합니다.
///  프로젝트의 실제 destroy 응답 흐름에 맞춰 OnEntityDestroyConfirmed 부분을 연결하세요.)
/// </summary>
//[RequireComponent(typeof(Collider))]
public class ConveyorEndTrigger : MonoBehaviour
{
    [SerializeField] private ConveyorBeltController belt;

    // 중복 신고 방지용. 이미 destroy 요청을 보낸 NetworkID는 다시 안 보냄.
    private readonly System.Collections.Generic.HashSet<long> requestedIds = new();

    private void Reset()
    {
        // 콜라이더가 트리거로 세팅되어 있는지 놓치기 쉬우니 기본값으로 강제
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent(out CatchableObj catchable)) return;

        long networkId = catchable.NetworkId;

        if (requestedIds.Contains(networkId)) return; // 이미 처리 중인 재료
        requestedIds.Add(networkId);

        // 1) 벨트 이동 관리에서 즉시 제거 (더 이상 컨베이어가 옮기지 않도록)
        if (belt != null)
        {
            belt.UnregisterItem(networkId);
        }

        // 2) 서버에 도착 알림 전송
        SendEntityDestroyPacket(networkId);
    }

    private void SendEntityDestroyPacket(long networkId)
    {
        if (ServerManager.Instance == null)
        {
            Debug.LogError("[ConveyorEndTrigger] ServerManager.Instance not found.");
            return;
        }

        EntityDestroyPacket packet = new()
        {
            EntityId = networkId
        };

        byte[] sendBuffer = PacketSerializer.Serialize(packet);
        ServerManager.Instance.SendData(sendBuffer).Forget();

        Debug.Log($"[ConveyorEndTrigger] Reached belt end, requested destroy. NetworkID: {networkId}");
    }

    /// <summary>
    /// 서버로부터 파괴 확정 응답을 받았을 때 호출 (핸들러 등록은 IngredientNetworkBridge류에서).
    /// requestedIds 정리 + 실제 풀 반환은 여기서 수행하거나, ObjectPoolManager 쪽에 위임.
    /// </summary>
    public void OnEntityDestroyConfirmed(long networkId)
    {
        requestedIds.Remove(networkId);
    }
}