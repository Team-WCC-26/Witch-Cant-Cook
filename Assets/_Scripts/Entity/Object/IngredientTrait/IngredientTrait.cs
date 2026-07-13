using Protocol;
using Server;
using UnityEngine;

public class IngredientTrait : MonoBehaviour
{
    /// <summary>
    /// 오브젝트 풀로 Ingredient를 반납함
    /// </summary>
    /// <param name="catchable"></param>
    public void PushIngredientToPool(CatchableObj catchable)
    {
        if (catchable == null)
        {
            Debug.LogError("IngredientTrait: PushIngredientToPool - catchable is null");
            return;
        }

        EntityDestroyPacket packet = new()
        {
            EntityId = catchable.NetworkId
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));

    }

}
