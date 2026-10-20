using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

//[UpdateInGroup(typeof(InitializationSystemGroup))]
[DisableAutoCreation] // Historical comparison only; never installed in a normal game's World.
public partial class IngredientSpawnSystem : SystemBase
{
    private const string DefaultDishAddress = "Default_Dish";
    private static readonly ProfilerMarker RequestMarker = new("Ingredient.SpawnRequest.Process");
    private static readonly ProfilerMarker EntityCommandsMarker = new("Ingredient.Entity.RecordCommands");
    private static readonly ProfilerMarker PlaybackMarker = new("Ingredient.Entity.Playback");

    private ObjectNetworkRouter objectRouter;
    protected override void OnUpdate()
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        if (objectRouter == null)
            objectRouter = UnityEngine.Object.FindFirstObjectByType<ObjectNetworkRouter>();
        if (objectRouter == null) return;

        using var requestScope = RequestMarker.Auto();
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        if (PairedPoolingEnabled)
            ProcessPooledRequests(ecb);
        else
        {
            foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<IngredientSpawnRequest>>().WithEntityAccess())
            {
                ProcessSpawn(request.ValueRO, ecb);
                ecb.DestroyEntity(requestEntity);
            }
        }

        using (PlaybackMarker.Auto())
            ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    private void ProcessSpawn(IngredientSpawnRequest request, EntityCommandBuffer ecb)
    {
        int reqID = request.IngredientID;
        long netID = request.NetworkID;
        Vector3 reqPos = request.Position;
        Quaternion reqRot = request.Rotation;

        bool isIngredient = DataManager.Instance.GetIngredient().TryGet(reqID, out Ingredient ingredientRaw);
        Recipe recipeRaw = null;

        if (!isIngredient && !DataManager.Instance.GetRecipe().TryGet(reqID, out recipeRaw))
        {
            Debug.LogError($"[SpawnSystem] Neither Ingredient nor Recipe data was found. ID: {reqID}");
            return;
        }

        string targetKey = isIngredient
            ? ingredientRaw.prefabName
            : ResolveDishAddress(recipeRaw);

        if (string.IsNullOrEmpty(targetKey))
        {
            return;
        }

        // 프리팹 생성 요청
        GameObject spawnedObj = ObjectPoolManager.Instance.Pop(targetKey, reqPos, reqRot,
            out CatchableObj catchable, out IngredientReaction reaction);

        if (spawnedObj != null)
        {
            // Assign identity before router registration can attach this food to a plate.
            FoodRecipeIdentity.Initialize(catchable, ingredientRaw, recipeRaw);
            // The pool resets before spawn data is assigned; initialize processing HP from the new ingredient.
            if (isIngredient)
                reaction?.ResetForPool();
            // 3. 네트워크 오브젝트 등록 및 일반 재료 ECS 컴포넌트 주입
            RegisterNetworkObject(spawnedObj, catchable, netID);

            if (isIngredient && PairedPoolingEnabled)
                ActivatePooledEntity(spawnedObj, ingredientRaw, netID, reqPos, reqRot);
            else if (isIngredient)
            {
                InjectIngredientECSComponents(
                    ecb,
                    ingredientRaw,
                    netID,
                    reqPos,
                    reqRot
                );
            }

            ObjectPoolManager.Instance.activeObjDict.Add(netID, spawnedObj);
            if (isIngredient && catchable != null)
                IngredientEgg.PrepareSpawnedInside(reqID, reqPos, catchable);

            var belt = ConveyorBeltRegistry.TryGetBeltById(request.ConveyId, out ConveyorBeltController beltController);
            if (beltController != null)
            {
                beltController.RegisterItem(netID, spawnedObj.transform, spawnedObj);
            }
        }
        else
        {
            Debug.LogError($"[SpawnSystem] Pool spawn failed. Key: {targetKey}");
        }

    }

    private static string ResolveDishAddress(Recipe recipe)
    {
        ResourceManager resourceManager = ResourceManager.Instance;
        if (resourceManager == null)
        {
            Debug.LogError("[SpawnSystem] ResourceManager was not found while resolving a dish prefab.");
            return null;
        }

        if (resourceManager.TryGetAsset<GameObject>(recipe.prefabName, out _))
            return recipe.prefabName;

        if (resourceManager.TryGetAsset<GameObject>(DefaultDishAddress, out _))
        {
            Debug.LogWarning(
                $"[SpawnSystem] Dish prefab was not found. RecipeId: {recipe.id}, " +
                $"Address: {recipe.prefabName}. Using {DefaultDishAddress}.");
            return DefaultDishAddress;
        }

        Debug.LogError(
            $"[SpawnSystem] Dish prefab and default prefab were not found. " +
            $"RecipeId: {recipe.id}, Address: {recipe.prefabName}, Default: {DefaultDishAddress}");
        return null;
    }

    private void RegisterNetworkObject(GameObject spawnedObj, CatchableObj catchable, long networkID)
    {
        if (catchable != null)
        {
            catchable.NetworkId = networkID;
            objectRouter.Add(networkID, catchable);
        }
        else
        {
            Debug.LogWarning($"[SpawnSystem] CatchableObj missing. NetworkID: {networkID}, Object: {spawnedObj.name}");
        }
    }

    private static void InjectIngredientECSComponents(
        EntityCommandBuffer ecb,
        Ingredient ingredientRaw,
        long networkID,
        Vector3 position,
        UnityEngine.Quaternion rotation)
    {
        using var entityCommandsScope = EntityCommandsMarker.Auto();
        var statId = ingredientRaw.statID;
        var statRaw = DataManager.Instance.GetIngredientStat().Get(statId);

        if (statRaw != null)
        {
            Entity newEntity = ecb.CreateEntity();

            // 기획 데이터 및 트랜스폼 설정 (위치와 회전을 동시에 설정)
            ecb.AddComponent(newEntity, new IngredientInfo { ID = ingredientRaw.id, Name = ingredientRaw.name });
            ecb.AddComponent(newEntity, new Health { Current = statRaw.hp, Max = statRaw.hp });
            ecb.AddComponent(newEntity, new IngredientPhysics { Weight = statRaw.weight, Throwing = DataManager.ParseEnum<ThrowingType>(ingredientRaw.throwing, ThrowingType.parabola) });
            ecb.AddComponent(newEntity, new IngredientCombat { Damage = statRaw.damage, Tag = ingredientRaw.tag });

            // 위치와 회전값 동시 주입
            ecb.AddComponent(newEntity, LocalTransform.FromPositionRotation(position, rotation));
            // 멀티플레이 식별 ID 주입
            ecb.AddComponent(newEntity, new NetworkID { Value = networkID });

            // 원격 동기화 초기값 세팅
            ecb.AddComponent(newEntity, new NetworkRemoteSync
            {
                TargetPosition = position,
                TargetRotation = rotation,
                InterpolationSpeed = 15f
            });
        }
    }
}
