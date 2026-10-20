using System;
using System.Collections.Generic;
using MemoryPack;
using Protocol;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

/// <summary>Exercises actual packet handlers and production preparation without a server.</summary>
[InitializeOnLoad]
public static class TEST_IngredientGoSpawnValidation
{
    private const string Key = "TEST.IngredientGoSpawnValidation";
    private static double deadline;
    static TEST_IngredientGoSpawnValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        deadline = EditorApplication.timeSinceStartup + 180;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void RunBatchValidation()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity batch mode.");
        TEST_PerformanceSceneEditor.CreateForBatch();
        var spawner = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<TEST_PerformanceSpawner>());
        spawner.FindProperty("autoSpawn").boolValue = false;
        spawner.ApplyModifiedPropertiesWithoutUndo();
        SessionState.SetBool(Key, true);
        deadline = EditorApplication.timeSinceStartup + 180;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Production spawn validation timed out.");
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<TEST_PerformanceBootstrap>();
            if (bootstrap == null || bootstrap.HasFailed) throw new Exception(bootstrap != null ? bootstrap.Status : "Missing bootstrap");
            if (!bootstrap.IsReady) return;
            EditorApplication.update -= Tick;
            Validate(bootstrap);
            Finish(true, "50-per-prefab preparation, FIFO packet spawn, server IDs/rotation, conveyor packet, recipe fallback, router/pool return, reuse, cancellation, zero ingredient Entities");
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }

    private static void Validate(TEST_PerformanceBootstrap bootstrap)
    {
        // Disabled networking component: Awake installs the real processor; OnEnable never subscribes.
        var owner = new GameObject("TEST_ProductionIngredientBridge");
        owner.SetActive(false);
        var bridge = owner.AddComponent<IngredientNetworkBridge>();
        bridge.enabled = false;
        owner.SetActive(true);
        if (bridge.Spawner == null) throw new Exception("Bridge did not install its production processor");
        var processor = bridge.Spawner;
        processor.Configure(bootstrap.Router, true, 50);
        var pool = ObjectPoolManager.Instance;
        // A request arriving before preparation must stay a value in the queue.
        const long firstId = 9_007_199_254_741_123L;
        var packet = new IngredientSpawnPacket
        {
            EntityId = firstId, IngredientID = 10300,
            Position = new System.Numerics.Vector3(-30f, 2f, -20f),
            Quaternion = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, .7f)
        };
        bridge.OnIngredientSpawnReceived(MemoryPackSerializer.Serialize(packet));
        if (processor.PendingCount != 1 || pool.activeObjDict.Count != 0) throw new Exception("Packet bypassed the queue");
        processor.ProcessPending();
        if (!processor.IsReady || processor.PendingCount != 0) throw new Exception("Preparation/queue did not complete");
        CatchableObj first = VerifyFood(bootstrap, firstId, 10300);
        Vector3 position = new(packet.Position.X, packet.Position.Y, packet.Position.Z);
        Quaternion rotation = new(packet.Quaternion.X, packet.Quaternion.Y, packet.Quaternion.Z, packet.Quaternion.W);
        if (Vector3.Distance(first.transform.position, position) > .001f || Quaternion.Angle(first.transform.rotation, rotation) > .01f)
            throw new Exception("Packet placement changed");
        var keys = new HashSet<string>();
        foreach (Ingredient ingredient in DataManager.Instance.GetIngredient().GetAll())
            if (ingredient != null && !string.IsNullOrEmpty(ingredient.prefabName) &&
                ResourceManager.Instance.TryGetAsset<GameObject>(ingredient.prefabName, out _) && keys.Add(ingredient.prefabName))
            {
                int expected = ingredient.prefabName == first.gameObject.name ? 49 : 50;
                if (pool.GetPooledCount(ingredient.prefabName) != expected) throw new Exception("Prewarm quantity mismatch: " + ingredient.prefabName);
            }
        pool.Push(first.gameObject);
        VerifyReturned(bootstrap, firstId, first);
        // Reuse with a different full-width server identity; exhaust the pool once to prove membership.
        var preparedIds = new HashSet<int>();
        foreach (Transform child in first.transform.parent)
            if (child.gameObject.name == first.gameObject.name) preparedIds.Add(child.gameObject.GetInstanceID());
        packet.EntityId = firstId + 1;
        bridge.OnIngredientSpawnReceived(MemoryPackSerializer.Serialize(packet));
        processor.ProcessPending();
        CatchableObj second = VerifyFood(bootstrap, packet.EntityId, 10300);
        if (!preparedIds.Contains(second.gameObject.GetInstanceID())) throw new Exception("Prepared GO was not reused");
        pool.Push(second.gameObject);
        VerifyReturned(bootstrap, packet.EntityId, second);

        ConveyorBeltController belt = UnityEngine.Object.FindFirstObjectByType<ConveyorBeltController>();
        var spawnOwner = new GameObject("TEST_ConveyorPacketPoint");
        spawnOwner.SetActive(false);
        spawnOwner.transform.SetPositionAndRotation(belt.Path.Evaluate(2f).point, Quaternion.Euler(0, 90, 0));
        var point = spawnOwner.AddComponent<ConveyorSpawnPoint>();
        var pointSettings = new SerializedObject(point);
        pointSettings.FindProperty("beltId").intValue = belt.BeltId;
        pointSettings.ApplyModifiedPropertiesWithoutUndo();
        spawnOwner.SetActive(true);
        var conveyorPacket = new IngredientConveySpawnPacket { EntityId = firstId + 2, IngredientId = 10300, ConveyId = belt.BeltId };
        bridge.OnConveySpawnReceived(MemoryPackSerializer.Serialize(conveyorPacket));
        processor.ProcessPending();
        CatchableObj conveyed = VerifyFood(bootstrap, conveyorPacket.EntityId, 10300);
        if (!belt.IsRegistered(conveyorPacket.EntityId)) throw new Exception("Conveyor packet registration missing");
        pool.Push(conveyed.gameObject);
        VerifyReturned(bootstrap, conveyorPacket.EntityId, conveyed);
        if (belt.IsRegistered(conveyorPacket.EntityId) || ConveyorBeltRegistry.IsOwnedByAnyBelt(conveyorPacket.EntityId))
            throw new Exception("Pool return retained conveyor ownership");

        // Current data also contains recipe IDs in the ingredient table. Preserve that precedence;
        // exercise the original recipe-only branch with an isolated in-memory row, then restore it.
        var recipeTable = DataManager.Instance.GetRecipe();
        List<Recipe> originalRecipes = recipeTable.GetAll();
        var recipe = new Recipe { id = int.MaxValue - 123, name = "TEST recipe-only identity", prefabName = "TEST_Missing_Dish_Fallback" };
        var recipeRows = new List<Recipe>(originalRecipes) { recipe };
        recipeTable.SetData(recipeRows);
        try
        {
            processor.Enqueue(new IngredientSpawnCommand { IngredientID = recipe.id, NetworkID = firstId + 3,
                Position = new Vector3(-30, 2, -20), Rotation = Quaternion.identity });
            processor.ProcessPending();
            CatchableObj dish = VerifyFood(bootstrap, firstId + 3, recipe.id);
            if (dish.gameObject.name != "Default_Dish" || dish.GetComponent<FoodRecipeIdentity>().RecipeId != recipe.id)
                throw new Exception("Recipe identity or default dish fallback changed");
            pool.Push(dish.gameObject);
            VerifyReturned(bootstrap, firstId + 3, dish);
            if (dish.GetComponent<FoodRecipeIdentity>().RecipeId != 0) throw new Exception("Returned dish retained recipe identity");
        }
        finally { recipeTable.SetData(originalRecipes); }

        processor.Enqueue(new IngredientSpawnCommand { IngredientID = 10300, NetworkID = firstId + 4, Rotation = Quaternion.identity });
        processor.CancelPending();
        processor.ProcessPending();
        if (pool.activeObjDict.Count != 0 || bootstrap.Router.catchableDics.Count != 0) throw new Exception("Cancellation left active food");
        World world = World.DefaultGameObjectInjectionWorld;
        using var ingredients = world.EntityManager.CreateEntityQuery(typeof(IngredientInfo));
        using var requests = world.EntityManager.CreateEntityQuery(typeof(IngredientSpawnRequest));
        using var cleanup = world.EntityManager.CreateEntityQuery(typeof(IngredientEntityDestroyRequest));
        if (!ingredients.IsEmptyIgnoreFilter || !requests.IsEmptyIgnoreFilter || !cleanup.IsEmptyIgnoreFilter ||
            world.GetExistingSystemManaged<IngredientSpawnSystem>() != null || world.GetExistingSystemManaged<IngredientEntityCleanupSystem>() != null)
            throw new Exception("Production path installed legacy ingredient ECS state");
        if (UnityEngine.Object.FindAnyObjectByType<Server.ServerManager>() != null) throw new Exception("Unexpected ServerManager");
    }

    private static CatchableObj VerifyFood(TEST_PerformanceBootstrap bootstrap, long id, int dataId)
    {
        if (!bootstrap.Router.TryGet(id, out CatchableObj food) || food.NetworkId != id || food.Data == null || food.Data.id != dataId ||
            !food.gameObject.activeSelf || !ObjectPoolManager.Instance.activeObjDict.TryGetValue(id, out UnityEngine.Object active) || active != food.gameObject)
            throw new Exception("Identity/data/registration mismatch: " + id);
        return food;
    }

    private static void VerifyReturned(TEST_PerformanceBootstrap bootstrap, long id, CatchableObj food)
    {
        if (food.gameObject.activeSelf || food.NetworkId != 0 || bootstrap.Router.TryGet(id, out _) || ObjectPoolManager.Instance.activeObjDict.ContainsKey(id))
            throw new Exception("Pool return retained active identity: " + id);
    }

    private static void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Finish(false, message + "\n" + stack); }

    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        Debug.Log($"[Ingredient GO Validation] {(success ? "PASS" : "FAIL")}: {message}");
        EditorApplication.Exit(success ? 0 : 1);
    }
}
