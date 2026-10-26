using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public struct IngredientSpawnCommand
{
    public int IngredientID;
    public long NetworkID;
    public Vector3 Position;
    public Quaternion Rotation;
    public int ConveyId;
}

// Scene-owned consumer. Incoming server identity and placement are copied without creating Entities.
[DefaultExecutionOrder(-9000)]
[DisallowMultipleComponent]
public sealed class IngredientSpawnProcessor : MonoBehaviour
{
    private const string DefaultDishAddress = "Default_Dish";
    private static readonly ProfilerMarker ProcessMarker = new("Ingredient.GO.ProcessRequests");
    private static readonly ProfilerMarker PrewarmMarker = new("Ingredient.GO.Prewarm");
    private static ObjectPoolManager preparedPool;
    private readonly Queue<IngredientSpawnCommand> requests = new(500);
    private ObjectNetworkRouter objectRouter;
    public int PendingCount => requests.Count;
    public void ClearPending() => requests.Clear();

    public void Enqueue(in IngredientSpawnCommand request)
    {
        if (!StageReturnFlow.IsSettlementActive) requests.Enqueue(request);
    }
    private void Update() => ProcessPending();

    public void ProcessPending()
    {
        if (StageReturnFlow.IsSettlementActive) return;
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded ||
            ResourceManager.Instance == null || !ResourceManager.Instance.IsPreloaded || ObjectPoolManager.Instance == null)
            return;
        // TitleScene prepares the pool before map activation. A direct scene start prepares it once here.
        if (preparedPool != ObjectPoolManager.Instance && !PrewarmIngredients()) return;
        if (requests.Count == 0) return;
        if (objectRouter == null) objectRouter = FindFirstObjectByType<ObjectNetworkRouter>();
        if (objectRouter == null) return;
        using var scope = ProcessMarker.Auto();
        int count = requests.Count;
        for (int i = 0; i < count; i++) Spawn(requests.Dequeue());
    }

    public static bool PrewarmIngredients(int countPerPrefab = 50)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded ||
            ResourceManager.Instance == null || !ResourceManager.Instance.IsPreloaded || ObjectPoolManager.Instance == null)
            return false;
        using var scope = PrewarmMarker.Auto();
        var keys = new HashSet<string>();
        ObjectPoolManager pool = ObjectPoolManager.Instance;
        foreach (Ingredient ingredient in DataManager.Instance.GetIngredient().GetAll())
        {
            if (ingredient == null || string.IsNullOrEmpty(ingredient.prefabName) || !keys.Add(ingredient.prefabName)) continue;
            if (!ResourceManager.Instance.TryGetAsset<GameObject>(ingredient.prefabName, out _))
            {
                Debug.LogWarning($"[IngredientSpawn] Prewarm prefab is not loaded: {ingredient.prefabName}");
                continue;
            }
            int missing = Mathf.Max(0, countPerPrefab) - pool.GetPooledCount(ingredient.prefabName);
            if (missing > 0) pool.PrewarmPool(ingredient.prefabName, missing);
        }
        preparedPool = pool;
        return true;
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
        // Preserve the former ECS consumer's GO initialization and registration order.
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
