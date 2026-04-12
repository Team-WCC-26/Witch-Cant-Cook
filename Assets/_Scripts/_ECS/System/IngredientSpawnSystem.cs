using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class IngredientSpawnSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded)
            return;

        // 메인 스레드에서 엔티티 명령을 예약하기 위한 ECB 생성
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 프리팹 정보를 담고 있는 싱글톤 데이터 가져오기 (미리 설정되어 있어야 함)
        if (!SystemAPI.TryGetSingleton<IngredientSpawnConfig>(out var config)) return;

        // 4. 모든 스폰 요청 엔티티를 순회
        foreach (var (request, requestEntity) in SystemAPI.Query<IngredientSpawnRequest>().WithEntityAccess())
        {
            // [데이터 로드] 원본 클래스 데이터 가져오기
            var ingredientRaw = DataManager.Instance.GetIngredient().Get(request.IngredientID);

            // ingredientRaw.stat_id를 이용해 스탯 정보도 가져옴
            int statId = int.Parse(ingredientRaw.stat_id);
            var statRaw = DataManager.Instance.GetIngredientStat().Get(statId);

            if (ingredientRaw != null && statRaw != null)
            {
                // [엔티티 생성] 프리팹 복제
                Entity newEntity = ecb.Instantiate(config.prefabEntity);

                // [데이터 매핑] 클래스 데이터를 구조체 컴포넌트로 주입
                // 1. 기본 정보
                ecb.AddComponent(newEntity, new IngredientInfo
                {
                    ID = ingredientRaw.id,
                    Name = ingredientRaw.name
                });

                // 2. 체력 정보
                ecb.AddComponent(newEntity, new Health
                {
                    Current = statRaw.hp,
                    Max = statRaw.hp
                });

                // 3. 물리 정보
                ecb.AddComponent(newEntity, new IngredientPhysics
                {
                    Weight = statRaw.weight,
                    Throwing = DataManager.ParseEnum<ThrowingType>(ingredientRaw.throwing, ThrowingType.Parabolic)
                });

                // 4. 전투 정보
                ecb.AddComponent(newEntity, new IngredientCombat
                {
                    Damage = statRaw.damage,
                    TagID = int.Parse(ingredientRaw.tag) // 태그가 문자열이라면 변환 필요
                });

                // [위치 설정] 요청된 좌표로 이동
                ecb.SetComponent(newEntity, LocalTransform.FromPosition(request.Position));
            }

            // [요청 삭제] 처리가 완료된 요청 엔티티는 파기
            ecb.DestroyEntity(requestEntity);
        }

        // 예약된 명령 실행 및 메모리 해제
        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}