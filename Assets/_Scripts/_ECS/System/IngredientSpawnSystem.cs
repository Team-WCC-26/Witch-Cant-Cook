using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

//[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class IngredientSpawnSystem : SystemBase
{
    private const string DefaultDishAddress = "Default_Dish";

    private ObjectNetworkRouter objectRouter;
    protected override void OnUpdate()
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        if (objectRouter == null)
            objectRouter = Object.FindFirstObjectByType<ObjectNetworkRouter>();
        if (objectRouter == null) return;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<IngredientSpawnRequest>>().WithEntityAccess())
        {
            int reqID = request.ValueRO.IngredientID;
            long netID = request.ValueRO.NetworkID;
            Vector3 reqPos = request.ValueRO.Position;
            Quaternion reqRot = request.ValueRO.Rotation;

            bool isIngredient = DataManager.Instance.GetIngredient().TryGet(reqID, out Ingredient ingredientRaw);
            Recipe recipeRaw = null;

            if (!isIngredient && !DataManager.Instance.GetRecipe().TryGet(reqID, out recipeRaw))
            {
                Debug.LogError($"[SpawnSystem] Neither Ingredient nor Recipe data was found. ID: {reqID}");
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            string targetKey = isIngredient
                ? ingredientRaw.prefabName
                : ResolveDishAddress(recipeRaw);

            if (string.IsNullOrEmpty(targetKey))
            {
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            // 프리팹 생성 요청
            GameObject spawnedObj = ObjectPoolManager.Instance.Pop(targetKey, reqPos, reqRot);

            if (spawnedObj != null)
            {
                // Assign identity before router registration can attach this food to a plate.
                FoodRecipeIdentity.Initialize(spawnedObj.GetComponent<CatchableObj>(), ingredientRaw, recipeRaw);
                // 3. 네트워크 오브젝트 등록 및 일반 재료 ECS 컴포넌트 주입
                RegisterNetworkObject(spawnedObj, netID);

                if (isIngredient)
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

                var belt = ConveyorBeltRegistry.TryGetBeltById(request.ValueRO.ConveyId, out ConveyorBeltController beltController);
                if (beltController != null)
                {
                    beltController.RegisterItem(netID, spawnedObj.transform, spawnedObj);
                }
            }
            else
            {
                Debug.LogError($"[SpawnSystem] Pool spawn failed. Key: {targetKey}");
            }

            ecb.DestroyEntity(requestEntity);
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
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

    private void RegisterNetworkObject(GameObject spawnedObj, long networkID)
    {
        CatchableObj catchable = spawnedObj.GetComponent<CatchableObj>();
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
