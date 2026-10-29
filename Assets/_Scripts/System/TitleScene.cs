using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleScene : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Button BTN_start;
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private RectTransform spinner;
    [SerializeField] private TMP_Text loadingStatusText;

    [Header("Rotation Settings")]
    [SerializeField] private float rotateSpeed = 100f;

    private const string MainSceneName = "MainScene";
    [Header("Ingredient Prewarm")]
    [SerializeField, Min(0)] private int ingredientPrewarmCount = 50;
    [SerializeField, Min(5f)] private float initializationTimeoutSeconds = 180f;

    private readonly Dictionary<string, int> _prewarmTargets = new()
    {
        // Pool keys must be individual prefab addresses, not the Tools group/label.
        { "Knife", 5 },
        { "Plate", 5 },
        { "Pan", 5 }
    };

    private bool isLoading;
    private float loadingStartedAt;
    private float phaseStartedAt;
    private float nextStatusUpdateAt;
    private string loadingPhase = "Waiting to start";
    private string latestLoadingIssue;
    private string sceneProgress;
    private readonly StringBuilder statusBuilder = new();
    private Button retryButton;

    private void OnEnable() => Application.logMessageReceived += OnLogMessage;

    private void OnDisable() => Application.logMessageReceived -= OnLogMessage;

    void Start()
    {
        PrepareLoadingText();
        BTN_start.onClick.AddListener(OnClickStartBTN);
        loadingPanel.SetActive(false);
    }

    private void PrepareLoadingText()
    {
        // UI_Load was authored as 100x100, which forced its debug text down to 12px.
        if (loadingPanel.transform is RectTransform panelRect)
        {
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
        }
        if (loadingStatusText == null)
        {
            foreach (TMP_Text candidate in loadingPanel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (candidate.name != "TMP_load") continue;
                loadingStatusText = candidate;
                break;
            }
        }

        if (loadingStatusText == null)
        {
            var textObject = new GameObject("LoadingDebugText", typeof(RectTransform));
            textObject.transform.SetParent(loadingPanel.transform, false);
            loadingStatusText = textObject.AddComponent<TextMeshProUGUI>();
        }

        loadingStatusText.gameObject.SetActive(true);
        loadingStatusText.transform.SetAsLastSibling();
        loadingStatusText.raycastTarget = false;
        loadingStatusText.richText = false;
        loadingStatusText.color = Color.white;
        loadingStatusText.alignment = TextAlignmentOptions.TopLeft;
        loadingStatusText.enableAutoSizing = false;
        loadingStatusText.fontSize = 30f;
        loadingStatusText.overflowMode = TextOverflowModes.Truncate;
        RectTransform rect = loadingStatusText.rectTransform;
        rect.anchorMin = new Vector2(.04f, .14f);
        rect.anchorMax = new Vector2(.96f, .85f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        retryButton = Instantiate(BTN_start, loadingPanel.transform);
        retryButton.name = "RetryLoading";
        retryButton.onClick = new Button.ButtonClickedEvent();
        retryButton.onClick.AddListener(OnClickStartBTN);
        RectTransform retryRect = (RectTransform)retryButton.transform;
        retryRect.anchorMin = retryRect.anchorMax = new Vector2(.5f, .06f);
        retryRect.anchoredPosition = Vector2.zero;
        retryRect.sizeDelta = new Vector2(300f, 60f);
        TMP_Text retryLabel = retryButton.GetComponentInChildren<TMP_Text>(true);
        if (retryLabel != null)
        {
            retryLabel.text = "Retry loading";
            retryLabel.enableAutoSizing = false;
            retryLabel.fontSize = 28f;
        }
        retryButton.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!isLoading) return;
        if (spinner != null) spinner.Rotate(0, 0, -rotateSpeed * Time.unscaledDeltaTime);
        if (Time.realtimeSinceStartup < nextStatusUpdateAt) return;
        nextStatusUpdateAt = Time.realtimeSinceStartup + .2f;
        RefreshLoadingText();
    }

    private void SetLoadingPhase(string phase)
    {
        loadingPhase = phase;
        phaseStartedAt = Time.realtimeSinceStartup;
        Debug.Log($"[TitleLoading] {phase}");
        RefreshLoadingText();
    }

    private void RefreshLoadingText()
    {
        if (loadingStatusText == null) return;
        float now = Time.realtimeSinceStartup;
        statusBuilder.Clear();
        statusBuilder.AppendLine($"Loading: {loadingPhase}");
        statusBuilder.AppendLine($"Elapsed: {now - loadingStartedAt:F1}s | This step: {now - phaseStartedAt:F1}s");

        // Read the existing load state; never restart data/resource initialization for diagnostics.
        DataManager data = FindFirstObjectByType<DataManager>();
        if (data == null) statusBuilder.AppendLine("Tables: waiting for DataManager");
        else
        {
            statusBuilder.AppendLine($"Tables: {data.LoadingStatus}");
            statusBuilder.AppendLine($"Rows: Ingredient {data.GetIngredient().Count}, Stat {data.GetIngredientStat().Count}, Recipe {data.GetRecipe().Count}");
            statusBuilder.AppendLine($"Rows: Combination {data.GetIngredientCombination().Count}, Group {data.GetIngredientGroup().Count}, Stage {data.GetStage().Count}");
        }

        ResourceManager resources = FindFirstObjectByType<ResourceManager>();
        statusBuilder.AppendLine(resources == null ? "Assets: waiting for ResourceManager" : $"Assets: {resources.LoadingStatus}");
        statusBuilder.AppendLine($"Sprites: {SpriteCache.LoadingStatus}");
        if (!string.IsNullOrEmpty(sceneProgress)) statusBuilder.AppendLine(sceneProgress);
        if (!string.IsNullOrEmpty(latestLoadingIssue)) statusBuilder.AppendLine($"Latest issue: {latestLoadingIssue}");
        loadingStatusText.text = statusBuilder.ToString();
    }

    private void OnLogMessage(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Warning && type != LogType.Error && type != LogType.Exception) return;
        if (!isLoading && !message.Contains("[DataManager]") && !message.Contains("[GSpread]") &&
            !message.Contains("[ResourceManager]") && !message.Contains("Sprite cache")) return;
        int lineEnd = message.IndexOfAny(new[] { '\r', '\n' });
        string firstLine = lineEnd >= 0 ? message.Substring(0, lineEnd) : message;
        latestLoadingIssue = firstLine.Length > 240 ? firstLine.Substring(0, 240) + "..." : firstLine;
    }

    void OnClickStartBTN()
    {
        if (isLoading) return;
        StartGameSequence().Forget();
    }

    private async UniTask StartGameSequence()
    {
        CancellationToken token = this.GetCancellationTokenOnDestroy();
        isLoading = true;
        loadingStartedAt = Time.realtimeSinceStartup;
        latestLoadingIssue = null;
        sceneProgress = null;
        retryButton.gameObject.SetActive(false);
        BTN_start.interactable = false;
        loadingPanel.SetActive(true);

        try
        {
            SetLoadingPhase("Waiting for tables");
            DataManager data = FindFirstObjectByType<DataManager>();
            if (data == null) throw new InvalidOperationException("DataManager is missing from the starting scene.");
            if (data.HasLoadingFailed) data.InitWithProgress(null, null);
            await WaitForInitialization(() => data.IsDataLoaded, () => data.LoadingError, token);
            SetLoadingPhase("Waiting for Addressables / sprites");
            ResourceManager resources = FindFirstObjectByType<ResourceManager>();
            if (resources == null) throw new InvalidOperationException("ResourceManager is missing from the starting scene.");
            if (resources.HasLoadingFailed) resources.Init().Forget(error => Debug.LogException(error));
            await WaitForInitialization(() => resources.IsPreloaded, () => resources.LoadingError, token);

            SetLoadingPhase($"Prewarming ingredient pools ({ingredientPrewarmCount} per prefab)");
            // Give the diagnostic label a rendered frame before synchronous pool creation.
            await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate, token);
            IngredientSpawnProcessor.PrewarmIngredients(ingredientPrewarmCount);

            foreach (var pair in _prewarmTargets)
            {
                SetLoadingPhase($"Prewarming pool: {pair.Key} ({pair.Value})");
                await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate, token);
                if (!resources.TryGetAsset<GameObject>(pair.Key, out _))
                    throw new InvalidOperationException($"Tool prefab is not loaded: {pair.Key}. Check its Addressables address.");
                ObjectPoolManager pool = ObjectPoolManager.Instance;
                int missing = Mathf.Max(0, pair.Value - pool.GetPooledCount(pair.Key));
                if (missing > 0) pool.PrewarmPool(pair.Key, missing);
            }
            SetLoadingPhase("Loading MainScene");
            if (!await LoadSceneAsync(MainSceneName))
                throw new InvalidOperationException($"Scene could not be loaded: {MainSceneName}");

            isLoading = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Scene activation destroys the title UI.
        }
        catch (Exception error)
        {
            if (this == null) return;
            latestLoadingIssue = error.Message;
            SetLoadingPhase("FAILED - see Latest issue");
            Debug.LogException(error, this);
            RefreshLoadingText();
            isLoading = false;
            BTN_start.interactable = true;
            retryButton.gameObject.SetActive(true);
        }
    }

    private async UniTask WaitForInitialization(Func<bool> isComplete, Func<string> getError, CancellationToken token)
    {
        float deadline = Time.realtimeSinceStartup + initializationTimeoutSeconds;
        while (!isComplete())
        {
            string error = getError();
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            if (Time.realtimeSinceStartup >= deadline)
                throw new TimeoutException($"{loadingPhase} exceeded {initializationTimeoutSeconds:0}s.");
            await UniTask.Yield(token);
        }
    }

    public async UniTask<bool> LoadSceneAsync(string sceneName)
    {
        CancellationToken token = this.GetCancellationTokenOnDestroy();
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneName);
        if (loadOp == null) return false;
        loadOp.allowSceneActivation = false;
        while (loadOp.progress < .9f)
        {
            sceneProgress = $"Scene {sceneName}: {Mathf.Clamp01(loadOp.progress / .9f):P0}";
            await UniTask.Yield(token);
        }

        sceneProgress = $"Scene {sceneName}: 100% - activation delay (1s)";
        await UniTask.Delay(TimeSpan.FromSeconds(1), ignoreTimeScale: true, cancellationToken: token);
        sceneProgress = $"Scene {sceneName}: activating";
        RefreshLoadingText();
        loadOp.allowSceneActivation = true;
        // Normal title destruction during activation must not cancel post-load pool setup.
        while (!loadOp.isDone) await UniTask.Yield();
        return true;
    }
}
