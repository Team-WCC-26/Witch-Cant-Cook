using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-scoped A/B selection. This is never created in a production scene.</summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed partial class TEST_ECSComparison : MonoBehaviour
{
    public const string SceneName = "ECSComparisonTestScene";
    public enum Backend { HybridECS, GameObjectOnly, HybridECSBatchedPooled, GameObjectCentralized }
    [SerializeField] private Backend backend = Backend.GameObjectCentralized;
    [SerializeField] private int randomSeed = 12345;

    public Backend ActiveBackend { get; private set; }
    public bool UsesECS => ActiveBackend == Backend.HybridECS || ActiveBackend == Backend.HybridECSBatchedPooled;
    public bool UsesCentralized => ActiveBackend == Backend.GameObjectCentralized;
    public bool UsesPairedPooling => ActiveBackend == Backend.HybridECSBatchedPooled;
    public string InitializationError { get; private set; }
    public bool IsInitialized { get; private set; }
    private bool removedDefaultWorld;
    private bool quitting;
    private bool seeded;
    private UnityEngine.Random.State previousRandomState;

    private void Awake()
    {
        ActiveBackend = backend; // Locked for the entire Play session, including after pool return.
        try
        {
            if (gameObject.scene.name != SceneName || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Start a fresh Play session with ECSComparisonTestScene loaded alone.");
            if (FindAnyObjectByType<GameManager>() != null || FindAnyObjectByType<Server.ServerManager>() != null)
                throw new InvalidOperationException("The comparison scene must not contain GameManager or ServerManager.");
            previousRandomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(randomSeed);
            seeded = true;
            if (UsesECS)
            {
                World world = World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) throw new InvalidOperationException("The pooled comparison requires a default ECS World.");
                IngredientEntityLifecycle.EnableComparison(world, UsesPairedPooling);
            }
            // Preserve the normal project's World in the production-path comparison.
            // Only the historical GO-only baseline removes it.
            if (!UsesECS && !UsesCentralized)
            {
                World world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
                    World.DefaultGameObjectInjectionWorld = null;
                    world.Dispose();
                    removedDefaultWorld = true;
                }
            }
            IsInitialized = true;
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
            Debug.LogError($"[ECSComparison] Initialization stopped: {exception}", this);
        }
    }

    private void OnApplicationQuit() => quitting = true;

    private void OnDestroy()
    {
        if (UsesECS) IngredientEntityLifecycle.DetachPool();
        if (seeded) UnityEngine.Random.state = previousRandomState;
        bool restoreWorld = !quitting && Application.isPlaying;
#if UNITY_EDITOR
        restoreWorld &= UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
#endif
        if (UsesPairedPooling && restoreWorld)
            IngredientEntityLifecycle.PooledSpawnSystem?.DisablePairedPooling();
        // Normal mode switching requires stopping Play. If the scene/component is unloaded while
        // still playing, restore the standard bootstrap so this test does not leave ECS disabled.
        if (removedDefaultWorld && restoreWorld && World.DefaultGameObjectInjectionWorld == null)
            DefaultWorldInitialization.Initialize("Default World");
    }
}
