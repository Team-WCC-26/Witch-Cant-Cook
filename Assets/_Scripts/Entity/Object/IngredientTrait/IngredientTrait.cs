using Protocol;
using Server;
using UnityEngine;

public abstract class IngredientTrait : MonoBehaviour
{
    public bool IsTraitAvailable
    {
        get
        {
            IngredientReaction reaction = GetComponentInParent<IngredientReaction>(true);
            return isActiveAndEnabled && (reaction == null || !reaction.IsActionCompleted(IngredientAction.Cut));
        }
    }

    private void OnEnable()
    {
        if (!IsTraitAvailable) { enabled = false; return; }
        StartTrait();
    }

    private void OnDisable()
    {
        StopTrait();
    }

    protected virtual void StartTrait() { }
    protected virtual void StopTrait() { }

    /// <summary>
    /// 오브젝트 풀로 Ingredient를 반납함 - 실제로는 Destroy 패킷 전송만 함
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
