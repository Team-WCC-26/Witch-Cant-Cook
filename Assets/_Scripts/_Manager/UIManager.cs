using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum eUIPosition
{
    /// <summary> 0 : 가장 밑에 깔리는 배경화면</summary>
    Background,

    /// <summary> 1 : 화면 상/하단의 안내 바</summary>
    Navigator,

    /// <summary> 2 : 팝업창</summary>
    Popup,

    /// <summary> 3 : 가장 위에서 모든걸 가려주는 UI.</summary>
    Overlap
}

public class UIManager : Singleton<UIManager>
{
    private List<Transform> parents;
    public bool HasUIParents => parents != null && parents.Exists(parent => parent != null);
    private List<UIBase> uiList = new();
    private readonly Dictionary<string, Task<UIBase>> pendingLoads = new();

    //public UILoading LoadingScreen;
    public static TaskCompletionSource<bool> SceneChangeTask;

    public static void SetParents(List<Transform> parents)
    {
        Instance.parents = parents;
    }

    private async Task<UIBase> CreateUIAsync<T>(string address) where T : UIBase
    {
        ResourceManager resources = ResourceManager.Instance;
        if (resources == null)
            throw new System.InvalidOperationException("[UIManager] ResourceManager is required to load UI.");
        GameObject asset = await resources.LoadAsync<GameObject>(address);
        if (asset == null || !asset.TryGetComponent<T>(out var prefab))
            throw new System.InvalidOperationException($"[UIManager] Addressable UI prefab missing or invalid: {address}.");
        if (this == null) throw new System.InvalidOperationException("[UIManager] Manager was destroyed while loading UI.");
        UIBase ui = Instantiate(prefab, ResolveParent(prefab.uiPosition));
        ui.name = address;
        uiList.Add(ui);
        return ui;
    }
    private Transform ResolveParent(eUIPosition position)
    {
        int index = (int)position;
        if (parents != null)
        {
            if (index >= 0 && index < parents.Count && parents[index] != null)
                return parents[index];

            // Existing scenes use one shared UI parent instead of separate layer containers.
            foreach (Transform parent in parents)
                if (parent != null) return parent;
        }
        throw new System.InvalidOperationException("[UIManager] No UI parent is configured. Assign UICanvas Parents.");
    }

    /// <typeparam name="T">UIBase를 상속받은 클래스 이름</typeparam>
    /// <param name="param">원하는 모든 형태의 변수 전달 가능</param>
    /// <returns>T 반환</returns>
    public async static Task<T> Show<T>(params object[] param) where T : UIBase
    {
        var manager = Instance;
        if (manager == null)
            throw new System.InvalidOperationException("[UIManager] Add UIManager to the scene before showing UI.");
        manager.uiList.RemoveAll(obj => obj == null);

        UIBase ui = manager.uiList.Find(obj => obj.name == typeof(T).ToString());

        if (ui == null)
        {
            string address = typeof(T).Name;
            if (!manager.pendingLoads.TryGetValue(address, out Task<UIBase> loading))
            {
                loading = manager.CreateUIAsync<T>(address);
                manager.pendingLoads.Add(address, loading);
            }
            try { ui = await loading; }
            finally { manager.pendingLoads.Remove(address); }
        }
        ui.gameObject.SetActive(ui.isActiveInCreated);
        ui.opened?.Invoke(param);
        ui.isActiveInCreated = true;

        return (T)ui;
    }

    /// <summary>
    /// UIBase의 bool값에 따라 setactive false 또는 파괴
    /// </summary>
    /// <typeparam name="T">UIBase를 상속받은 클래스 이름</typeparam>
    /// <param name="param">원하는 모든 형태의 변수 전달 가능</param>
    public static void Hide<T>(params object[] param) where T : UIBase
    {
        Instance.uiList.RemoveAll(obj => obj == null);
        var ui = Instance.uiList.Find(obj => obj.name == typeof(T).ToString());
        if (ui != null)
        {
            ui.closed.Invoke(param);
            if (ui.isDestroyAtClosed)
            {
                Instance.uiList.Remove(ui);
                Destroy(ui.gameObject);
            }
            else
            {
                ui.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 동적 생성한 UI를 가져오는 메서드
    /// </summary>
    /// <typeparam name="T">UI 스크립트 이름</typeparam>
    /// <returns>UI 스크립트</returns>
    public static T Get<T>() where T : UIBase
    {
        string type = typeof(T).Name;

        Instance.uiList.RemoveAll(obj => obj == null);
        return (T)Instance.uiList.Find(obj => obj.name == typeof(T).ToString());
    }

    /// <summary>
    /// UI 존재 여부
    /// </summary>
    /// <typeparam name="T">UI 스크립트 이름</typeparam>
    /// <returns></returns>
    public static bool IsOpened<T>() where T : UIBase
    {
        Instance.uiList.RemoveAll(obj => obj == null);
        return Instance.uiList.Exists(obj => obj.name == typeof(T).ToString());
    }
}