using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class EscUI : UIBase
{
    [SerializeField] private Button respawnBtn;
    public event Action RespawnRequested;

    #region Unity Callbacks
    protected override void Awake()
    {
        base.Awake();
        foreach (Button btn in GetComponentsInChildren<Button>(true))
            btn.enabled = btn == respawnBtn;
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = graphic == respawnBtn.targetGraphic;
        respawnBtn.onClick.AddListener(() => RespawnRequested?.Invoke());
    }
    #endregion

    /// <summary>Update the respawn button availability.</summary>
    public void SetRespawnAvailable(bool available)
    {
        respawnBtn.interactable = available;
    }
}
