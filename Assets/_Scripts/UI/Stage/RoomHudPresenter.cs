using System;
using UnityEngine;

// Loads existing prefabs; never constructs UI controls or a Canvas at runtime.
public sealed class RoomHudPresenter : MonoBehaviour
{
    private RoomHudSession session;
    private StageReturnFlow returnFlow;
    private UIManager manager;
    private int shownMode = -1;
    private bool dirty = true;
    private bool switching;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var owner = new GameObject(nameof(RoomHudPresenter));
        DontDestroyOnLoad(owner);
        owner.AddComponent<RoomHudPresenter>();
    }

    private void OnEnable()
    {
        session = RoomHudSession.Instance;
        session.Changed += OnChanged;
        returnFlow = StageReturnFlow.Instance;
        returnFlow.Changed += OnChanged;
        dirty = true;
        shownMode = -1;
    }

    private void OnDisable()
    {
        if (session != null) session.Changed -= OnChanged;
        if (returnFlow != null) returnFlow.Changed -= OnChanged;
    }

    private void OnChanged() { dirty = true; Present(); }
    private void Update() { if (dirty) Present(); }

    private async void Present()
    {
        if (switching || session == null) return;
        if (manager == null) manager = FindFirstObjectByType<UIManager>();
        if (manager == null || !manager.HasUIParents) return;
        int mode = !session.Lobby.IsInRoom ? 0 : returnFlow.State.ShowsSettlement ? 3 : session.Lobby.IsActive ? 1 : 2;
        dirty = false;
        if (shownMode == mode) return;
        switching = true;
        try
        {
            if (mode != 1) UIManager.Hide<LobbyHudUI>();
            if (mode != 2) UIManager.Hide<StageHudUI>();
            if (mode != 1 && mode != 2) UIManager.Hide<InteractionUI>();
            if (mode == 1) await UIManager.Show<LobbyHudUI>();
            if (mode == 2) await UIManager.Show<StageHudUI>(true);
            if (mode == 1 || mode == 2) await UIManager.Show<InteractionUI>();
            shownMode = mode;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
        finally { switching = false; }
    }
}
