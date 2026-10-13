using UnityEngine;

public sealed class OvenButton : MonoBehaviour, IInteractTarget, IPunchReceiver
{
    [SerializeField] private OvenInteraction oven;

    public EntityCategory Category => EntityCategory.Oven;
    public EntityCategory AcceptedCategories => EntityCategory.All;

    public void OnPunched(PlayerBrain player)
    {
        if (oven != null) oven.PressButton(player);
    }
}