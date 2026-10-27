using UnityEngine;

public sealed class TrashCanBtn : MonoBehaviour, IInteractTarget, IPunchReceiver
{
    [SerializeField] private TrashCanInteraction trashCan;

    public EntityCategory Category => EntityCategory.TrashCan;
    public EntityCategory AcceptedCategories => EntityCategory.All;

    #region Unity Callbacks

    private void Awake()
    {
        if (trashCan == null) trashCan = GetComponentInParent<TrashCanInteraction>();
    }

    #endregion

    public void OnPunched(PlayerBrain player)
    {
        if (trashCan != null) trashCan.PressButton(player);
    }
}
