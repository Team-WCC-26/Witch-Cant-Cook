using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

//[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class IngredientSpawnSystem : SystemBase
{
    protected override void OnUpdate()
    {
        Debug.Log("[TEST] IngredientSpawnSystem Update");
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<IngredientSpawnRequest>>().WithEntityAccess())
        {
            Debug.Log("[TEST] SpawnRequest Query 진입");
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
            GameObject spawnedObj = ObjectPoolManager.Instance.Pop(targetKey, reqPos, reqRot);
            Debug.Log($"[TEST] Spawned Object: {spawnedObj}");

            if (spawnedObj != null)
            {
                // 3. ECS 컴포넌트 주입 및 딕셔너리 세팅 로직 호출
                InjectECSComponents(
                    ecb,
                    spawnedObj,
                    reqID,
                    netID,
                    reqPos,
                    reqRot
                );
                ObjectPoolManager.Instance.activeObjDict.Add(netID, spawnedObj);

                if (spawnedObj.TryGetComponent(out CatchableObj catchObj))
                {
                    catchObj.Data = ingredientRaw;
                }
            }
            else
            {
                Debug.LogError($"[SpawnSystem] 풀링 스폰 실패. 리소스가 로드되지 않았습니다: {targetKey}");
            }

            ecb.DestroyEntity(requestEntity);
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    private void InjectECSComponents(EntityCommandBuffer ecb,GameObject spawnedObj, int ingredientID, long networkID, Vector3 position, UnityEngine.Quaternion rotation)
    {
        //TEST
        CatchableObj catchable = spawnedObj.GetComponent<CatchableObj>();
        if (catchable != null)
        {
            catchable.NetworkId = networkID;
            ObjectNetworkRouter.Instance.Add(networkID, catchable);
        }
        else
        {
            Debug.LogWarning($"[SpawnSystem] CatchableObj 없음. NetworkID: {networkID}, Object: {spawnedObj.name}");
        }

        var ingredientRaw = DataManager.Instance.GetIngredient().Get(ingredientID);
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