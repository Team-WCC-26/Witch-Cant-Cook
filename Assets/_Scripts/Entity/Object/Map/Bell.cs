using System;
using UnityEngine;

public class Bell : MonoBehaviour, IInteractTarget, IPunchReceiver
{
    public static event Action OnBellRung;
    public event Action Rung;

    public EntityCategory Category => EntityCategory.None;
    public EntityCategory AcceptedCategories => EntityCategory.Player | EntityCategory.Knife;

    #region Unity Callbacks
    private void OnCollisionEnter(Collision collision) => Ring();
    #endregion

    public void OnPunched(PlayerBrain player) => Ring();

    public void Ring()
    {
        Debug.Log("[Bell] 충돌 발생");
        Rung?.Invoke();
        OnBellRung?.Invoke();
    }
}
