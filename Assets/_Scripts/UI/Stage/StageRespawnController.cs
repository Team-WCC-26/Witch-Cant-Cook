using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
public sealed class StageRespawnController : MonoBehaviour
{
    private EscUI popup;
    private bool wantsPopup;
    private bool loading;
    private int inputBlockedFrame = -1;
    private static StageRespawnController instance;
    public static bool IsInputBlocked => instance != null &&
        (instance.wantsPopup || instance.inputBlockedFrame == Time.frameCount);

    #region Unity Callbacks
    private void Awake()
    {
        instance = this;
    }

    private void Update()
    {
        if (!CanOpen())
        {
            Close();
            return;
        }
        Keyboard keyboard = Keyboard.current;
        if (wantsPopup && popup != null) popup.SetRespawnAvailable(CanRespawn());
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (wantsPopup) Close();
            else
            {
                wantsPopup = true;
                if (popup != null)
                {
                    popup.SetRespawnAvailable(CanRespawn());
                    popup.SetActive(true);
                }
                else if (!loading) LoadPopup();
            }
            return;
        }
        if (wantsPopup && popup != null && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            TryRespawn();
    }

    private static bool CanRespawn()
    {
        PlayerSpawnManager players = PlayerSpawnManager.Instance;
        return players != null && !string.IsNullOrEmpty(players.MyID) &&
            players.TryGetPlayer(players.MyID, out PlayerBrain player) && player != null &&
            player.Health != null && !player.Health.IsRagdoll;
    }

    private void TryRespawn()
    {
        if (!wantsPopup || !CanOpen() || !CanRespawn()) return;
        PlayerSpawnManager players = PlayerSpawnManager.Instance;
        if (players != null && players.RespawnPlayer(players.MyID)) Close();
    }

    private void OnDisable()
    {
        Close();
    }

    private void OnDestroy()
    {
        if (popup != null) Destroy(popup.gameObject);
        if (instance == this) instance = null;
    }
    #endregion

    private static bool CanOpen()
    {
        return RoomHudSession.Instance.State.IsStageRunning &&
            StageReturnFlow.Instance.State.Phase == StageReturnPhase.Playing &&
            !StageReturnFlow.IsInputBlocked;
    }

    private async void LoadPopup()
    {
        loading = true;
        try
        {
            EscUI loadedPopup = await UIManager.Show<EscUI>();
            if (this == null)
            {
                if (loadedPopup != null) loadedPopup.SetActive(false);
                return;
            }
            popup = loadedPopup;
            popup.RespawnRequested += TryRespawn;
            popup.SetRespawnAvailable(CanRespawn());
            popup.SetActive(wantsPopup && isActiveAndEnabled && CanOpen());
        }
        catch (Exception error)
        {
            if (this != null) Close();
            Debug.LogException(error);
        }
        finally
        {
            if (this != null) loading = false;
        }
    }

    private void Close()
    {
        if (wantsPopup) inputBlockedFrame = Time.frameCount;
        wantsPopup = false;
        if (popup != null && popup.gameObject.activeSelf) popup.SetActive(false);
    }
}
