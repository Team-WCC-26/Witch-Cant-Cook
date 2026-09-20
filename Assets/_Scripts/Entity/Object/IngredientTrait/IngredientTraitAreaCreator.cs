using UnityEngine;
using Unity.Mathematics;

public class IngredientTraitAreaCreator : MonoBehaviour
{
    [SerializeField] private float yFloor = 0.11f;
    /// <summary>
    /// 영역 생성 요청 패킷 전송(스폰 자체는 서버가)
    /// </summary>
    public void CreateArea(Define.eIngredient eIngredient)
    {
        Debug.Log($"[AreaCreator] 영역 생성  instance={GetInstanceID()}, pos={transform.position}, frame={Time.frameCount}");
        // 생성 요청 패킷 전송
        IngredientNetworkBridge.Instance.SendSpawnPacketToServer(
            (int)eIngredient,
            new float3(transform.position.x, yFloor, transform.position.z)
            );
    }

}
