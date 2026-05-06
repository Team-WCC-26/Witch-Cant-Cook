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

        if (!SystemAPI.TryGetSingletonBuffer<IngredientAddressBuffer>(out var addressBuffer)) return;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<IngredientSpawnRequest>>().WithEntityAccess())
        {
            int reqID = request.ValueRO.IngredientID;
            Vector3 reqPos = request.ValueRO.Position;

            // 1. 버퍼에서 ID에 맞는 어드레서블 키 찾기
            FixedString64Bytes targetKey = default;
            foreach (var item in addressBuffer)
            {
                if (item.IngredientID == reqID)
                {
                    targetKey = item.AddressKey;
                    break;
                }
            }

            if (!targetKey.IsEmpty)
            {
                // 2. 어드레서블 비동기 소환 시작
                var handle = Addressables.InstantiateAsync(targetKey.ToString(), reqPos, Quaternion.identity);

                // 3. 소환 완료 시점(콜백)에 실행될 로직 정의
                handle.Completed += (AsyncOperationHandle<GameObject> op) =>
                {
                    if (op.Status == AsyncOperationStatus.Succeeded)
                    {
                        GameObject spawnedObj = op.Result;
                        InjectECSComponents(spawnedObj, reqID, reqPos); // 아래 분리된 함수 호출
                    }
                    else
                    {
                        Debug.LogError($"어드레서블 로드 실패: {targetKey}");
                    }
                };
            }
            else
            {
                Debug.LogWarning($"라이브러리에 ID {reqID}의 어드레서블 키가 없습니다.");
            }

            // 요청 처리가 끝났으니 (비동기 호출은 시작했으니) 요청 엔티티 삭제
            ecb.DestroyEntity(requestEntity);
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    // 콜백 안에서 코드가 너무 길어지는 것을 방지하기 위해 밖으로 빼낸 데이터 주입 함수
    private void InjectECSComponents(GameObject spawnedObj, int ingredientID, Vector3 position)
    {
        var ingredientRaw = DataManager.Instance.GetIngredient().Get(ingredientID);
        if (ingredientRaw == null) return;

        int statId = int.Parse(ingredientRaw.statID);
        var statRaw = DataManager.Instance.GetIngredientStat().Get(statId);

        if (statRaw != null)
        {
            // 하이브리드 연동: 게임 오브젝트에 연결될 새로운 엔티티 생성
            Entity newEntity = EntityManager.CreateEntity();

            // 기존 로직과 동일하게 ECS 데이터 세팅
            EntityManager.AddComponentData(newEntity, new IngredientInfo { ID = ingredientRaw.id, Name = ingredientRaw.name });
            EntityManager.AddComponentData(newEntity, new Health { Current = statRaw.hp, Max = statRaw.hp });
            EntityManager.AddComponentData(newEntity, new IngredientPhysics { Weight = statRaw.weight, Throwing = DataManager.ParseEnum<ThrowingType>(ingredientRaw.throwing, ThrowingType.parabola) });
            EntityManager.AddComponentData(newEntity, new IngredientCombat { Damage = statRaw.damage, Tag = ingredientRaw.tag });

            EntityManager.AddComponentData(newEntity, LocalTransform.FromPosition(position));

            // [선택 사항] GameObject와 Entity를 양방향으로 연결하는 컴포넌트가 필요하다면 여기서 추가
        }
    }
}