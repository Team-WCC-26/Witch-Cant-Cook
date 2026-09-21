using UnityEngine;

public abstract class MapObjInteraction : MonoBehaviour, IInteractTarget
{
    #region enum fields

    [SerializeField] private Define.eToolId eToolId;
    [SerializeField] private EntityCategory acceptedCategories = EntityCategory.None;

    public Define.eToolId ToolType => (Define.eToolId)eToolId;
    public EntityCategory Category => eToolId switch
    {
        Define.eToolId.Stove => EntityCategory.Stove,
        Define.eToolId.Pot => EntityCategory.Pot,
        Define.eToolId.PrepTable => EntityCategory.PrepTable,
        Define.eToolId.Oven => EntityCategory.Oven,
        _ => EntityCategory.None
    };
    public EntityCategory AcceptedCategories => acceptedCategories;

    #endregion

    #region network fields

    protected MapObjNetworkRouter Router { get; private set; }

    public int ToolId => (int)eToolId;
    private long networkId;
    public long NetworkId => networkId;

    public bool IsRegistered => networkId != 0;

    #endregion

    public virtual void InitializeRouter(MapObjNetworkRouter router)
    {
        Router = router;
    }

    public virtual void SetNetworkId(long entityId)
    {
        networkId = entityId;
    }
}