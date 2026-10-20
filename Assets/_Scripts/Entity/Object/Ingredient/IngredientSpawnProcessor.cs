using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>A value request; server identity and placement are copied without creating an Entity.</summary>
public struct IngredientSpawnCommand
{
    public int IngredientID;
    public long NetworkID;
    public Vector3 Position;
    public Quaternion Rotation;
    public int ConveyId;
}

[Serializable]
public struct IngredientPrewarmOverride
{
    public int IngredientId;
    [Min(0)] public int Count;
}

/// <summary>Scene-owned FIFO consumer. Reuses the existing pool and registration order.</summary>
[DefaultExecutionOrder(-9000)]
[DisallowMultipleComponent]
public sealed class IngredientSpawnProcessor : MonoBehaviour
{
    public const string PipelineVersion = "GameObjectCentralized_v1";
    private const string DefaultDishAddress = "Default_Dish";
    private static readonly ProfilerMarker EnqueueMarker = new("Ingredient.GO.Enqueue");
    private static readonly ProfilerMarker ProcessMarker = new("Ingredient.GO.ProcessRequests");
    private static readonly ProfilerMarker PrewarmMarker = new("Ingredient.GO.Prewarm");
    private readonly Queue<IngredientSpawnCommand> requests = new(500);
    private ObjectNetworkRouter objectRouter;
    private bool automaticPrewarm = true;
    private int defaultPrewarmCount = 50;
    private IngredientPrewarmOverride[] prewarmOverrides = Array.Empty<IngredientPrewarmOverride>();
    public int PendingCount => requests.Count;
    public bool IsReady { get; private set; }

    public void Configure(ObjectNetworkRouter router, bool prewarm, int defaultCount = 50,
        IngredientPrewarmOverride[] overrides = null)
    {
        objectRouter = router;
        automaticPrewarm = prewarm;
        defaultPrewarmCount = Mathf.Max(0, defaultCount);
        prewarmOverrides = overrides ?? Array.Empty<IngredientPrewarmOverride>();
    }

    public void Enqueue(in IngredientSpawnCommand request)
    {
        using var scope = EnqueueMarker.Auto();
        requests.Enqueue(request);
    }

    // Used by the isolated performance scene before returning its own entire batch.
    public void CancelPending() => requests.Clear();

    private void Update() => ProcessPending();

    public void ProcessPending()
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded ||
            ResourceManager.Instance == null || !ResourceManager.Instance.IsPreloaded || ObjectPoolManager.Instance == null)
            return;
        if (objectRouter == null) objectRouter = FindFirstObjectByType<ObjectNetworkRouter>();
        if (objectRouter == null) return;
        if (!IsReady)
        {
            if (automaticPrewarm) PrewarmIngredients();
            IsReady = true;
        }
        if (requests.Count == 0) return;
        using var scope = ProcessMarker.Auto();
        // Only requests already waiting at this processing point belong to this update.
        int count = requests.Count;
        for (int i = 0; i < count; i++) Spawn(requests.Dequeue());
    }

    private void PrewarmIngredients()
    {
        using var scope = PrewarmMarker.Auto();
        // Allocate only during loading. Multiple ingredient IDs can share one prefab pool.
        var targets = new Dictionary<string, int>();
        foreach (Ingredient ingredient in DataManager.Instance.GetIngredient().GetAll())
        {
            if (ingredient == null || string.IsNullOrEmpty(ingredient.prefabName)) continue;
            int count = defaultPrewarmCount;
            foreach (IngredientPrewarmOverride entry in prewarmOverrides)
                if (entry.IngredientId == ingredient.id) { count = Mathf.Max(0, entry.Count); break; }
            if (!targets.TryGetValue(ingredient.prefabName, out int previous) || count > previous)
                targets[ingredient.prefabName] = count;
        }
        foreach (var target in targets)
        {
            if (target.Value <= 0) continue;
            if (!ResourceManager.Instance.TryGetAsset<GameObject>(target.Key, out _))
            {
                Debug.LogWarning($"[IngredientSpawn] Prewarm prefab is not loaded: {target.Key}", this);
                continue;
            }
            ObjectPoolManager pool = ObjectPoolManager.Instance;
            int missing = target.Value - pool.GetPooledCount(target.Key);
            if (missing > 0) pool.PrewarmPool(target.Key, missing);
        }
    }

    private void Spawn(in IngredientSpawnCommand request)
    {
        bool isIngredient = DataManager.Instance.GetIngredient().TryGet(request.IngredientID, out Ingredient ingredient);
        Recipe recipe = null;
        if (!isIngredient && !DataManager.Instance.GetRecipe().TryGet(request.IngredientID, out recipe))
        {
            Debug.LogError($"[SpawnSystem] Neither Ingredient nor Recipe data was found. ID: {request.IngredientID}");
            return;
        }
        string key = isIngredient ? ingredient.prefabName : ResolveDishAddress(recipe);
        if (string.IsNullOrEmpty(key)) return;
        GameObject go = ObjectPoolManager.Instance.Pop(key, request.Position, request.Rotation,
            out CatchableObj catchable, out IngredientReaction reaction);
        if (go == null)
        {
            Debug.LogError($"[SpawnSystem] Pool spawn failed. Key: {key}");
            return;
        }
        FoodRecipeIdentity.Initialize(catchable, ingredient, recipe);
        if (isIngredient) reaction?.ResetForPool();
        if (catchable != null)
        {
            catchable.NetworkId = request.NetworkID;
            objectRouter.Add(request.NetworkID, catchable);
        }
        else Debug.LogWarning($"[SpawnSystem] CatchableObj missing. NetworkID: {request.NetworkID}, Object: {go.name}");
        ObjectPoolManager.Instance.activeObjDict.Add(request.NetworkID, go);
        if (isIngredient && catchable != null)
            IngredientEgg.PrepareSpawnedInside(request.IngredientID, request.Position, catchable);
        if (ConveyorBeltRegistry.TryGetBeltById(request.ConveyId, out ConveyorBeltController belt) && belt != null)
            belt.RegisterItem(request.NetworkID, go.transform, go);
    }

    private static string ResolveDishAddress(Recipe recipe)
    {
        ResourceManager resources = ResourceManager.Instance;
        if (resources.TryGetAsset<GameObject>(recipe.prefabName, out _)) return recipe.prefabName;
        if (resources.TryGetAsset<GameObject>(DefaultDishAddress, out _))
        {
            Debug.LogWarning($"[SpawnSystem] Dish prefab was not found. RecipeId: {recipe.id}, Address: {recipe.prefabName}. Using {DefaultDishAddress}.");
            return DefaultDishAddress;
        }
        Debug.LogError($"[SpawnSystem] Dish prefab and default prefab were not found. RecipeId: {recipe.id}, Address: {recipe.prefabName}, Default: {DefaultDishAddress}");
        return null;
    }
}
