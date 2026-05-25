using Server;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : Singleton<GameManager>
{
    public Dictionary<long, CatchableObj> catchableDics = new();

    public readonly string mainSceneName = "MainStage";

    #region Unity Life Cycles
    protected override void Awake()
    {
        base.Awake();
        InitBaseManagers();
    }
    #endregion

    #region Main Methods
    public void StartGame()
    {
        // TODO : 게임 시작 버튼시 호출

    }

    public void InitManager<T>() where T : Singleton<T>
    {
        if (Singleton<T>.IsInitialized) return;

        GameObject mngObj = new(typeof(T).Name);
        mngObj.transform.SetParent(transform, false);
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
        InitManager<StageManager>();
    }

    
    #endregion
}