using UnityEngine;

public class IngredientTraitAreaCreator : MonoBehaviour
{
    /// <summary>
    /// 영역 생성 요청 패킷 전송(스폰 자체는 서버가)
    /// </summary>
    public void CreateArea(Define.eIngredient eIngredient)
    {
        Debug.Log($"SpawnTearArea - Position: {transform.position}");

        // 생성 요청 패킷 전송
        IngredientNetworkBridge.Instance.SendSpawnPacketToServer(
            (int)eIngredient,
            transform.position
            );
    }

}
