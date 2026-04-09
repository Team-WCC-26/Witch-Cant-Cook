using UnityEngine;

[RequireComponent(typeof(LobbyRouter))]
public class LobbyRouterUI : SlotInteractHandler
{
    private LobbyRouter _lobbyRouter;

    protected override void Awake()
    {
        base.Awake();

        _lobbyRouter = GetComponent<LobbyRouter>();
    }


    #region Pointer Event

    public override void OnDoubleClick()
    {
    }

    public override void OnLeftClick()
    {
    }

    public override void OnRightClick()
    {
    }

    public override void OnPointerIn()
    {
    }

    public override void OnPointerOut()
    {
    }

    #endregion
}
