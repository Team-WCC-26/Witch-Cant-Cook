using UnityEngine;
using UnityEngine.UI;

public sealed class EscUI : UIBase
{
    #region Unity Callbacks
    protected override void Awake()
    {
        base.Awake();
        foreach (Button btn in GetComponentsInChildren<Button>(true))
            btn.enabled = false;
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }
    #endregion
}
