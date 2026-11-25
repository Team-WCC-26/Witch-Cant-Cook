using Cysharp.Threading.Tasks;
using Server;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : Singleton<GameManager>
{
    #region Unity Life Cycles
    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;
        InitBaseManagers();
    }

    private async void Start()
    {
        if (Instance != this) return;
        try
        {
            await ResourceManager.Instance.Init();
        }
        catch (System.Exception error)
        {
            Debug.LogError($"[GameManager] Resource initialization failed: {error.Message}");
        }
    }
    #endregion

    #region Main Methods
    public void StartGame()
    {
        // TODO : 게임 시작 버튼시 호출

    }

    public void InitManager<T>() where T : Singleton<T>
    {
        // Scene components can exist before Awake has set their singleton instance.
        // Reuse them so a configured DataManager/GSpreadReader is never replaced.
        if (FindFirstObjectByType<T>() != null) return;

        GameObject mngObj = new(typeof(T).Name);
        //mngObj.transform.SetParent(transform, false);
        mngObj.AddComponent<T>();
    }
    #endregion

    #region Sub Methods
    private void InitBaseManagers()
    {
        // 아직 다 추가한 것 아님
        InitManager<ServerManager>();
        InitManager<DataManager>();
        InitManager<ResourceManager>();
        InitManager<ObjectPoolManager>();
        InitManager<UIManager>();
        //InitManager<StageManager>();

    }

    
    #endregion
}