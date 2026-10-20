using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Offline initialization for the dedicated performance/comparison scenes. Never creates a server or GameManager.</summary>
[DisallowMultipleComponent]
public sealed class TEST_PerformanceBootstrap : MonoBehaviour
{
    public const string SceneName = "PerformanceTestScene";
    [SerializeField] private DataManager dataManager;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private ObjectPoolManager pool;
    [SerializeField] private ObjectNetworkRouter objectRouter;
    [SerializeField, Min(1f)] private float initializationTimeout = 60f;

    public bool IsReady { get; private set; }
    public bool HasFailed { get; private set; }
    public string Status { get; private set; } = "Initializing";
    public ObjectNetworkRouter Router => objectRouter;

    private readonly List<(Behaviour component, bool wasEnabled)> suspendedSessionComponents = new();
    private bool quitting;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private ConveyorSpawnTimingDebug[] timingDebuggers;
    private bool[] previousDebugEnabled;
#endif

    private async void Start()
    {
        try
        {
            TEST_ECSComparison comparison = GetComponent<TEST_ECSComparison>();
            bool comparisonScene = gameObject.scene.name == TEST_ECSComparison.SceneName && comparison != null;
            if ((gameObject.scene.name != SceneName && !comparisonScene) || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open a dedicated performance/comparison scene alone and start a fresh Play session.");
            if (comparison != null && (!comparison.IsInitialized || comparison.InitializationError != null))
                throw new InvalidOperationException(comparison.InitializationError ?? "Comparison mode was not initialized.");
            bool useEcs = comparison != null && comparison.UsesECS;
            if (FindAnyObjectByType<GameManager>() != null || FindAnyObjectByType<Server.ServerManager>() != null)
                throw new InvalidOperationException("GameManager/ServerManager must not be present in the performance scene.");
            if (dataManager == null || resourceManager == null || pool == null || objectRouter == null)
                throw new InvalidOperationException("Performance scene manager references are missing.");
            if (objectRouter.enabled)
                throw new InvalidOperationException("Keep ObjectNetworkRouter disabled on its active GameObject to prevent server subscription.");
            if (DataManager.Instance != dataManager || ResourceManager.Instance != resourceManager || ObjectPoolManager.Instance != pool)
                throw new InvalidOperationException("Another scene's managers are still alive. Restart Play from PerformanceTestScene.");

            // These are created by BeforeSceneLoad hooks even in this standalone test scene.
            // Find existing instances only: singleton getters would attempt server registration.
            SuspendSessionComponents<RoomHudPresenter>();
            SuspendSessionComponents<RoomHudSession>();
            SuspendSessionComponents<StageSelectionSession>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // AfterSceneLoad creates these before Start. Do this once, never scan them during measurement.
            timingDebuggers = FindObjectsByType<ConveyorSpawnTimingDebug>(FindObjectsSortMode.None);
            previousDebugEnabled = new bool[timingDebuggers.Length];
            for (int i = 0; i < timingDebuggers.Length; i++)
            {
                previousDebugEnabled[i] = timingDebuggers[i].enabled;
                timingDebuggers[i].enabled = false;
            }
#endif

            var token = this.GetCancellationTokenOnDestroy();
            float deadline = Time.realtimeSinceStartup + initializationTimeout;
            while (!dataManager.IsDataLoaded || (useEcs && (World.DefaultGameObjectInjectionWorld == null ||
                   !World.DefaultGameObjectInjectionWorld.IsCreated)))
            {
                if (Time.realtimeSinceStartup >= deadline)
                    throw new TimeoutException("DataManager/Entities initialization timed out. Check Resources/Json and Console errors.");
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            Status = "Loading existing Addressables through ResourceManager.Init";
            await resourceManager.Init().Timeout(TimeSpan.FromSeconds(initializationTimeout)).AttachExternalCancellation(token);
            if (!useEcs && comparison != null && !comparison.UsesCentralized && World.DefaultGameObjectInjectionWorld != null)
                throw new InvalidOperationException("An ECS World was recreated during GameObject-only initialization. Restart Play.");
            pool.InitRoot();
            if (useEcs) IngredientEntityLifecycle.AttachPool(pool);
            IsReady = true;
            Status = "Ready (offline)";
            Debug.Log("[PerformanceTest] Ready. Request creation and spawn are measured separately; no server is running.", this);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            HasFailed = true;
            Status = exception.Message;
            Debug.LogError($"[PerformanceTest] Initialization stopped: {exception}", this);
        }
    }

    private void SuspendSessionComponents<T>() where T : Behaviour
    {
        foreach (T component in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            suspendedSessionComponents.Add((component, component.enabled));
            component.enabled = false;
        }
    }

    private void OnApplicationQuit() => quitting = true;

    private void OnDestroy()
    {
        IngredientEntityLifecycle.DetachPool();
        bool restore = !quitting && Application.isPlaying;
#if UNITY_EDITOR
        restore &= UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
#endif
        // During Play teardown another session may already have been destroyed. Re-enabling
        // its presenter would recreate singleton GameObjects while Unity closes the scene.
        if (!restore) { suspendedSessionComponents.Clear(); return; }
        // Restore session sources before their presenters/listeners. Preserve already-disabled instances.
        for (int i = suspendedSessionComponents.Count - 1; i >= 0; i--)
        {
            var state = suspendedSessionComponents[i];
            if (state.component != null)
                state.component.enabled = state.wasEnabled;
        }
        suspendedSessionComponents.Clear();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (timingDebuggers == null) return;
        for (int i = 0; i < timingDebuggers.Length; i++)
            if (timingDebuggers[i] != null)
                timingDebuggers[i].enabled = previousDebugEnabled[i];
#endif
    }
}
