using UnityEngine;

public enum ToolId
{
    KitchenKnife = 10,
    FryingPan = 20,
    GasRange = 30,
    Pot = 40,
    Plate = 50,
    PrepTable = 60,
    Oven = 80,
    Stove = 90,
}

public abstract class MapObjInteraction : MonoBehaviour
{
    [SerializeField] private Define.eToolId eToolId;

    private long networkId;

    protected MapObjNetworkRouter Router { get; private set; }

    public int ToolId => (int)eToolId;
    public Define.eToolId ToolType => (Define.eToolId)eToolId;
    public long NetworkId => networkId;
    public bool IsRegistered => networkId != 0;

    public virtual void InitializeRouter(MapObjNetworkRouter router)
    {
        Router = router;
    }

    public virtual void SetNetworkId(long entityId)
    {
        networkId = entityId;
    }
}
