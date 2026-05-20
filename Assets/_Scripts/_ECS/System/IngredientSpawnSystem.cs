using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class IngredientSpawnSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<IngredientSpawnRequest>>().WithEntityAccess())
        {
            int reqID = request.ValueRO.IngredientID;
            long netID = request.ValueRO.NetworkID;
            Vector3 reqPos = request.ValueRO.Position;
            Quaternion reqRot = request.ValueRO.Rotation;

            var ingredientRaw = DataManager.Instance.GetIngredient().Get(reqID);
            if (ingredientRaw == null)
            {
                Debug.LogWarning($"[SpawnSystem] DataManager에 ID {reqID} 데이터가 없습니다.");
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            string targetKey = ingredientRaw.prefabName;

            // 프리팹 생성 요청
            var handle = Addressables.InstantiateAsync(targetKey, reqPos, reqRot);

            handle.Completed += (op) =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    GameObject spawnedObj = op.Result;
                    InjectECSComponents(spawnedObj, reqID, netID, reqPos, reqRot);
                }
                else
                {
                    Debug.LogError($"[SpawnSystem] 어드레서블 프리팹 로드 실패: {targetKey}");
                }
            };

            ecb.DestroyEntity(requestEntity);
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    private void InjectECSComponents(GameObject spawnedObj, int ingredientID, long networkID, Vector3 position, UnityEngine.Quaternion rotation)
    {
        //TEST
        CatchableObj catchable = spawnedObj.GetComponent<CatchableObj>();
        if (catchable != null)
        {
            GameManager.Instance.catchableDics[networkID] = catchable;
        }
        else
        {
            Debug.LogWarning($"[SpawnSystem] CatchableObj 없음. NetworkID: {networkID}, Object: {spawnedObj.name}");
        }

        var ingredientRaw = DataManager.Instance.GetIngredient().Get(ingredientID);
        var statId = int.Parse(ingredientRaw.statID);
        var statRaw = DataManager.Instance.GetIngredientStat().Get(statId);

        if (statRaw != null)
        {
            Entity newEntity = EntityManager.CreateEntity();

            // 기획 데이터 및 트랜스폼 설정 (위치와 회전을 동시에 설정)
            EntityManager.AddComponentData(newEntity, new IngredientInfo { ID = ingredientRaw.id, Name = ingredientRaw.name });
            EntityManager.AddComponentData(newEntity, new Health { Current = statRaw.hp, Max = statRaw.hp });
            EntityManager.AddComponentData(newEntity, new IngredientPhysics { Weight = statRaw.weight, Throwing = DataManager.ParseEnum<ThrowingType>(ingredientRaw.throwing, ThrowingType.parabola) });
            EntityManager.AddComponentData(newEntity, new IngredientCombat { Damage = statRaw.damage, Tag = ingredientRaw.tag });

            // 위치와 회전값 동시 주입
            EntityManager.AddComponentData(newEntity, LocalTransform.FromPositionRotation(position, rotation));

            // 멀티플레이 식별 ID 주입
            EntityManager.AddComponentData(newEntity, new NetworkID { Value = networkID });

            // 원격 동기화 초기값 세팅
            EntityManager.AddComponentData(newEntity, new NetworkRemoteSync
            {
                TargetPosition = position,
                TargetRotation = rotation,
                InterpolationSpeed = 15f
            });
        }
    }
}