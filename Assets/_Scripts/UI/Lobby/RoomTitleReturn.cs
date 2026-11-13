using System;
using Cysharp.Threading.Tasks;
using Server;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomTitleReturn
{
    public static bool IsRunning { get; private set; }

    public static async UniTask RunAsync(bool serverAlreadyRemovedPlayer = false)
    {
        if (IsRunning) return;
        IsRunning = true;
        try
        {
            const string titleScene = "TitleScene";
            if (!Application.CanStreamedLevelBeLoaded(titleScene))
                throw new InvalidOperationException("TitleScene is missing from the build scene list.");

            ServerManager server = ServerManager.Instance;
            if (server != null)
            {
                if (serverAlreadyRemovedPlayer) server.IsEnterRoom = false;
                else await server.ExitRoomAsync();
            }

            StageSelectionSession.Instance.OnRoomLeft();
            await SceneManager.LoadSceneAsync(titleScene);
            if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.ReturnRoomObjects();
        }
        finally
        {
            IsRunning = false;
        }
    }
}
