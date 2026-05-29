using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleScene : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Button BTN_start;
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private RectTransform spinner;

    [Header("Rotation Settings")]
    [SerializeField] private float rotateSpeed = 100f;

    [Header("Scene & Asset Settings")]
    [SerializeField] private string mainSceneName = "DirectPlayground";

    /// <summary>
    /// Pool prewarm 대상 - 임시
    /// Key = Addressable Key
    /// Value = 생성 개수
    /// </summary>
    private readonly Dictionary<string, int> _prewarmTargets = new()
    {
        { "Ingredients", 50 },
        { "Tools", 5 }
    };

    private bool isLoading = false;

    void Start()
    {
        BTN_start.onClick.AddListener(OnClickStartBTN);

        loadingPanel.SetActive(false);
    }

    private void Update()
    {
        if (isLoading && spinner != null)
        {
            spinner.Rotate(0, 0, -rotateSpeed * Time.deltaTime);
        }
    }

    void OnClickStartBTN()
    {
        StartGameSequence().Forget();
    }

    private async UniTaskVoid StartGameSequence()
    {
        isLoading = true;
        BTN_start.interactable = false;
        loadingPanel.SetActive(true);

        await UniTask.WaitUntil(() => DataManager.Instance.IsDataLoaded);


        // 1. map scene preload --------------------------------------
        bool isSceneLoadSuccess = await LoadSceneAsync(mainSceneName);

        if (!isSceneLoadSuccess)
        {
            Debug.LogError("[Preload Error] 씬 로드 실패");

            isLoading = false;
            BTN_start.interactable = true;
            loadingPanel.SetActive(false);

            // TODO : 실패 팝업
            return;
        }

        // 3. prewarm ---------------------------------------
        foreach (var pair in _prewarmTargets)
        {
            string key = pair.Key;
            int count = pair.Value;

            ObjectPoolManager.Instance.PrewarmPool(key, count);
        }

        isLoading = false;
    }
    // 어디다 둘지 몰라서 일단 여기다 둠..

    /// <summary>
    /// Scene 비동기 로드 
    /// </summary>
    /// <param name="sceneName"></param>
    /// <returns></returns>
    public async UniTask<bool> LoadSceneAsync(string sceneName)
    {
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneName);

        if (loadOp == null)
        {
            Debug.LogError($"[ResourceManager] Scene load 실패 (존재하지 않는 씬 이름): {sceneName}");
            return false;
        }

        loadOp.allowSceneActivation = false;

        // 유니티 특성상 allowSceneActivation이 false일 때 progress는 0.9에서 멈춤
        while (loadOp.progress < 0.9f)
        {
            await UniTask.Yield();
        }

        await UniTask.Delay(TimeSpan.FromSeconds(1.0f));

        loadOp.allowSceneActivation = true;

        while (!loadOp.isDone)
        {
            await UniTask.Yield();
        }

        return true;
    }
}
