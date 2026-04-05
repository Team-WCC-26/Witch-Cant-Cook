using UnityEngine;

public class GameManager : Singleton<GameManager>
{
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
        //게임 시작 버튼시 호출
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
        InitManager<DataManager>();
        InitManager<UIManager>();
        InitManager<StageManager>();
    }

    
    #endregion
}