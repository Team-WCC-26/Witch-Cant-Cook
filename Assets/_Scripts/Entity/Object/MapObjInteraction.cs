using UnityEngine;

public enum ToolId
{
    KitchenKnife = 10,
    FryingPan = 20,
    GasRange = 30,
    Pot = 40,
    Plate = 50,
    PrepTable = 60,
    Oven = 80
}

public abstract class MapObjInteraction : MonoBehaviour
{
    [SerializeField] private ToolId toolId;

    private long networkId;

    protected MapObjNetworkRouter Router { get; private set; }

    public int ToolId => (int)toolId;
    public ToolId ToolType => toolId;
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
