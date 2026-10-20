# Hybrid ECS / GameObject Only 비교

## 주기적 소량 스폰·반환 CSV 테스트

기존 `ECSComparisonTestScene`의 `TEST_ECSComparison`에서 아래 값을 설정한다.
게임·서버 코드는 수정하지 않으며, 테스트가 기존 스폰 경로에 요청을 넣고 기존 풀에 반환한다.

| Inspector 항목 | 기본값 | 의미 |
|---|---|---|
| Interval Spawn Seconds | 2 | 몇 초마다 요청할지 |
| Interval Spawn Count | 5 | 한 번에 생성할 개수 |
| Interval Lifetime Seconds | 10 | 실제 생성 확인부터 풀 반환까지 유지할 시간 |
| Interval Duration Seconds | 60 | 스폰 요청을 계속 발생시킬 시간 |
| Interval Prewarm Count | 50 | 반복 시작 전에 준비할 해당 재료 풀 수량 |
| Interval Baseline Seconds | 2 | 활성 0개 상태를 기록할 시간 |
| Interval Recovery Seconds | 2 | 전부 반환하고 정리가 끝난 후 기록할 시간 |
| Interval Frame Capacity | 262144 | 모든 반복에서 저장할 프레임 버퍼 상한 |
| CSV Repetitions | 기존 값 | 위 시나리오 반복 횟수. 비교 시 3 권장 |

`TEST_PerformanceSpawner`의 `Auto Spawn`은 끈다. Ingredient ID, Use Conveyor, 벨트와 간격은 기존 설정을 사용한다.
일괄 테스트의 `CSV Counts`, `CSV Sample Frames`, 수동 `Spawn Count/Prewarm Count`는 간격 테스트에 적용하지 않는다.
`CSV Settle Seconds`는 예열 후 안정화 대기 시간으로 계속 사용한다.

Play를 끈 상태에서 **Run INTERVAL BEFORE / AFTER (Prewarmed) and save CSV**를 누른다.
메뉴는 `Tools > Performance Tests > Run INTERVAL BEFORE-AFTER Prewarmed to CSV`이다.
기존 Prewarm `HybridECS`와 현재 `GameObjectCentralized`를 각각 새 Play 세션에서 실행하고 설정을 복원한다.
한 모드만 실행하려면 Backend와 `CSV Scenario = IntervalLifecycle`를 선택하고 Play 후 `Run this mode and save CSV`를 누른다.
기존의 BEFORE/AFTER 버튼은 기존 일괄 테스트를 계속 실행한다.

각 반복은 `Prewarm → Baseline → IntervalRunning → IntervalDraining → IntervalRecovered` 순서다.
Running에서는 t=0부터 주기적으로 소량 요청을 보낸다. 여러 차례 생성한 재료가 동시에 살아 있으며,
각 재료는 생성 확인 후 Lifetime이 지나면 반환한다. Duration이 끝나면 새 요청만 멈추고,
이미 생성한 재료는 남은 수명을 지킨 뒤 자연스럽게 반환한다. 마지막 ECS 정리까지 확인하고 Recovery를 기록한다.
기본값은 반복당 30번 × 5개 = 150개를 순차 생성한다. 동시 활성 수는 보통 약 25개이고 프레임 경계에 따라 잠시 더 많을 수 있다.
컨베이어는 매 요청마다 시작 부분으로 들어가며 기존 테스트 루프를 유지한다. 반환 기준은 벨트 끝이 아닌 Lifetime이다.

스폰·반환 시각은 프레임에서 관측하므로 설정값보다 늦을 수 있다. 지연된 요청을 버리지 않고 한 프레임에 최대 한 번의 burst만 보낸다.
스케줄 지연과 요청→생성 확인 지연은 별도로 기록한다. 서버 통신·직렬화·GPU 화면 표시 완료 시간은 포함하지 않는다.
풀 수량이 동시 필요량보다 작으면 기존 풀의 확장 비용도 기록한다. 예열이 충분한 비교에는 이를 피할 만큼 수량을 지정한다.

### 저장 파일

저장 위치: `Application.persistentDataPath/PerformanceTests/ECSComparisonInterval/<실행ID>/<모드>/`.
현재 PC에서는 `C:/Users/sj/AppData/LocalLow/DefaultCompany/Witch-Cant-Cook/PerformanceTests/ECSComparisonInterval/`이다.
기존 `Open last CSV folder` 버튼으로 열 수 있다.

| 파일 | 확인할 내용 |
|---|---|
| frames.csv | 프레임별 CPU·GC·Memory·Conveyor, 활성·대기 개수, 제출/생성/반환 누계 |
| summary.csv | 단계별 평균·p95·p99·최대 및 합계. 기존 컬럼은 유지하고 p99·반환 Marker를 추가 |
| events.csv | 재료별 새 NetworkID, GO Instance ID, 회차·burst, 제출/생성 확인/반환 프레임·시각, 지연·유지 시간·재사용 여부 |
| interval_summary.csv | 반복별 생성/반환 수, 실제 재사용 수, 최대 동시 활성, 요청→생성 p50/p95/p99/최대, 스케줄 지연, 긴 CPU 프레임 횟수 |
| settings.csv | 간격·수량·수명·예열·기간·버퍼·측정 범위 |

이 시나리오에서 `count`는 **회당 생성 개수**다. 동시 활성 개수는 `verified_objects`/`peak_active`를 본다.
`request_to_active_ms`는 테스트가 요청을 제출한 뒤 기존 경로에서 생성·등록됐음을 확인한 시간이다.
`reused_after_return`은 같은 반복에서 이미 활성화했던 GO가 다시 사용됐다는 뜻이며 예열 GO의 첫 사용은 포함하지 않는다.
ECS의 정리는 다음 업데이트에서 완료될 수 있어 반환 직후 Entity 수가 한 프레임 더 남을 수 있다.

주로 Running/Draining의 `main_thread_ms_p99`, `main_thread_ms_max`, 16.67/33.33ms 초과 횟수,
요청→생성 지연, `submit_ms_sum` + 모드별 처리 Marker, `interval_return_ms_sum/max`, GC와 메모리를 비교한다.
16.67/33.33ms 카운터는 실제로 각각 1000/60, 1000/30ms 초과를 센다. 구간 합계는 반복 길이·요청 수가 같은 경우 비교한다.
프레임 버퍼·이벤트 배열·추적 자료는 Baseline 전에 준비하고 기록 중 파일 IO·문자열 포맷·강제 GC는 하지 않는다.
수집 버퍼와 테스트 검증 비용은 측정에 포함되므로 두 모드의 설정을 같게 유지한다. Inspector 상태 표시는 초당 최대 2회 갱신한다.
중단 시 `Cancelled` 상태의 부분 CSV를 별도 폴더에 저장하고 대기 요청과 활성 재료를 정리한다.
프레임 버퍼가 부족하면 실패 상태로 부분 저장한다. 기간/반복이 길거나 FPS가 높으면 Interval Frame Capacity를 늘린다.

## 현재 본 게임: GO + Prewarm + 중앙 요청 처리

본 게임의 재료 스폰은 `Server Packet → IngredientNetworkBridge → IngredientSpawnCommand FIFO 큐 →
IngredientSpawnProcessor → 기존 ObjectPoolManager → 데이터/NetworkID/Router/계란/컨베이어 등록`이다.
요청 수량이나 시간을 채우기 위해 기다리지 않는다. 매 중앙 Update에서 이미 대기 중인 요청을 순서대로 처리한다.
서버 패킷과 NetworkID는 그대로이며, 잡기·던지기·물리·조리·컨베이어 이동 코드는 변경하지 않는다.

`IngredientNetworkBridge` Inspector의 `Prewarm Per Ingredient` 기본값은 **50**이다.
`Prewarm Overrides`에서 Ingredient ID별 수량을 덮어쓸 수 있다. 프리팹을 공유하는 ID들은 같은 풀을 사용하므로
그 키의 최대 목표 수량을 사용한다. 기존 비활성 풀 개수에서 부족한 만큼만 추가한다.
데이터와 ResourceManager preload가 완료되면 준비하고, 이후 첫 대기 요청을 소비한다.
TitleScene의 기존 Ingredients 준비 단계도 이 준비 완료를 기다린다. 풀 부족 시에는 기존 풀의 확장 동작을 유지한다.

### 적용 전후 CSV 비교

1. `ECSComparisonTestScene`만 열고 Play를 끈 상태에서 `TEST_PerformanceSpawner.Auto Spawn`을 끈다.
2. `TEST_ECSComparison`의 CSV Counts, Repetitions, Sample Frames, Settle Seconds를 설정한다.
   예: `50/100/200/500`, `3`, `120`, `2`. Ingredient ID, 배치와 컨베이어 설정은 두 세션에 공통이다.
3. **Run BEFORE / AFTER (Prewarmed) and save CSV** 버튼을 누른다.
   또는 `Tools > Performance Tests > Run BEFORE-AFTER Prewarmed to CSV`를 실행한다.
4. 적용 전 `HybridECS`, 적용 후 `GameObjectCentralized`가 각각 새로운 Play 세션에서 실행된다.
   이 버튼은 두 모드 모두 Prewarmed로 실행하고, 종료 후 원래 Inspector 설정을 복원한다.
5. 기존과 동일한 저장 경로의 같은 실행 폴더 아래 두 모드별 `summary.csv`, `frames.csv`, `settings.csv`가 생성된다.
   `Open last CSV folder`로 열 수 있다.

본 게임의 종류별 자동 50개 예열은 CSV에서는 **꺼진다**. CSV의 별도 Prewarm 단계가 각 case의 정확한 수량을 준비한다.
새 모드도 실제 production `IngredientSpawnProcessor`를 사용하며 테스트 코드에서 Prefab을 직접 생성하지 않는다.
현재 production 경로와 맞추기 위해 새 모드는 프로젝트의 기본 ECS World를 그대로 두되, 재료 시스템·요청/재료 Entity를 만들지 않는다.
기존 `GameObjectOnly`는 역사적 기준선으로서 기본 World를 제거하는 동작을 유지한다. 세 모드를 혼동하지 않는다.

| 비교 항목 | HybridECS (적용 전) | GameObjectCentralized (적용 후) |
|---|---|---|
| FirstSpawn / ReuseSpawn 작업 CPU | `submit_ms_sum + spawn_process_ms_sum` | `submit_ms_sum + central_spawn_ms_sum` |
| 긴 프레임 | `main_thread_ms_max`, `frame_interval_ms_p95` | 동일 |
| 정상 실행 중 | Spawned/Reused의 Main Thread, Physics, Conveyor | 동일 |
| GC/메모리 | `gc_alloc_bytes_sum`, `gc_used_bytes_last`, `total_allocated_bytes_last`, `object_count_last`, `mesh_bytes_last` | 동일 |
| 활성 재료 Entity | 테스트 수량 | 0 |

`central_spawn_ms`는 `Ingredient.GO.ProcessRequests` Marker다. Pool Marker는 그 하위 비용이므로 다시 더하지 않는다.
새 모드는 `spawn_pipeline=GameObjectCentralized_v1`로 식별한다. 기존 컬럼 이름과 모드 enum 0/1/2는 유지하고 새 모드 3과 컬럼만 추가했다.
이전 CSV와도 비교할 수 있으나 동일 실행 조건의 BEFORE/AFTER 결과를 우선한다.
기존 BOTH/ALL THREE 버튼은 기존 모드 조합을 유지한다. 아래 내용은 역사적 비교 모드 설명이다.

## 요청 묶음 + GameObject/Entity 재사용 비교

기존 씬의 `TEST_ECSComparison > Backend`에 `HybridECSBatchedPooled`를 추가했다. 기본 `HybridECS`, `GameObjectOnly`의 enum 값과 동작은 유지한다. 새 모드는 비교 씬에서 새 Play 세션을 시작할 때만 활성화하며 실제 게임에서는 기본적으로 꺼져 있다. 서버 패킷·Bridge·NetworkID 발급 코드는 변경하지 않았다.

1. `ECSComparisonTestScene`만 열고 Auto Spawn을 끈다. Ingredient ID·컨베이어·배치·해상도를 기존 실험과 같게 맞춘다.
2. `CSV Pool Preparation = Prewarmed`, Counts = 50/100/200/500, Repetitions = 3, Sample Frames = 120, Settle Seconds = 2로 맞춘다.
3. **Run ALL THREE modes and save CSV**를 누르면 기존 HybridECS, GameObjectOnly, 새 HybridECSBatchedPooled를 각각 새 Play 세션에서 실행한다. 기존 Run BOTH는 이전 두 모드만 실행한다.
4. 같은 실행 ID 아래 `HybridECSBatchedPooled/summary.csv`, `frames.csv`, `settings.csv`가 추가된다. 단독 실행은 Backend를 선택한 뒤 Play → Run this mode를 사용한다. 씬·Build Settings는 자동 저장하지 않는다.

새 모드의 흐름은 `TEST 스포너 → IngredientSpawnRequest 값을 담은 재사용 DynamicBuffer → 기존 IngredientSpawnSystem → 기존 ObjectPoolManager + 짝지은 Entity`이다. 요청별 Entity 생성은 생략하며 한 버퍼에 담긴 요청을 같은 업데이트에서 처리한다. 여러 프레임으로 스폰을 분산하지 않는다. 기존 단일 요청 Entity도 같은 스폰 함수로 처리한다.

반환 시 NetworkID와 재료 상태를 지우고 `IngredientPooledActive`를 비활성화한다. GameObject마다 같은 Entity를 유지하며, 다음 스폰에서는 서버/테스트가 새로 준 NetworkID와 데이터를 덮어쓴다. 이전 ID의 지연된 정리 요청은 새 ID의 Entity에 영향을 주지 않는다. 활성 재료 수와 풀 보관 Entity 수는 별개다. 관련 ECS 조회는 `IngredientPooledActive`를 포함해야 비활성 Entity가 제외된다. 이 태그만 추가한다고 모든 ECS 조회에서 자동 제외되는 것은 아니다.

`Prewarmed`는 GameObject와 Entity를 함께 준비한다. `Cold`는 첫 스폰에 둘을 생성한 뒤 반환·재사용한다. 각 CSV 수량·반복 시작 시 ClearPool로 비활성 GameObject와 짝 Entity를 함께 제거하므로 이전 수량이 다음 경우의 여유분이 되지 않는다. Entity 보관 메모리는 Baseline부터 포함되며 첫 스폰만 보고 총 비용·메모리 절감을 판단하면 안 된다.

| 새 기록 항목 | 의미 |
|---|---|
| `batch_process_ms_sum` | 요청 버퍼 및 기존 단일 요청 처리. spawn_process의 하위 비용 |
| `pooled_entity_create_ms_sum` | 풀 부족 시 Entity 생성. Prewarmed 첫 스폰과 재사용에서는 0 기대 |
| `pooled_entity_activate_ms_sum` | 재료 데이터·NetworkID 설정과 활성화 |
| `pooled_entity_return_ms_sum` | 비활성화·이전 ID 제거·상태 초기화 |
| `paired_entity_capacity_last` | 활성 + 비활성으로 보관하는 짝 Entity 수 |
| `paired_entity_created_total_last` | 해당 World 시작 이후 생성 누계 |
| `paired_entity_reused_total_last` | 해당 World 시작 이후 기존 Entity 활성화 누계. 예열 Entity 첫 활성화도 포함 |

원본 frames에는 마지막 세 열의 `_last` 없는 프레임별 값도 기록한다. 기존 모드에서 짝 Entity 열은 빈칸이다. 누계는 World 단위이므로 같은 case의 단계 간 차이로 비교한다. Prewarmed Baseline→Spawned와 Returned→Reused에서 생성 누계가 증가하지 않고, 재사용 누계가 수량만큼 증가해야 한다. 반환 후 verified_entities=0이어도 capacity는 유지된다. ClearPool 후 capacity=0이어야 한다.

첫 스폰/재사용의 `submit_ms_sum + spawn_process_ms_sum`, `main_thread_ms_max`, GC 할당을 기존 HybridECS와 비교한다. 하위 EntityPool/batch/Pool Marker를 다시 더하지 않는다. 기존 `entity_record_ms_sum`은 새 모드에서 0이고 ECB Playback도 비어 있을 수 있으므로, 이 둘만 줄었다고 전체 비용이 사라졌다고 해석하지 않는다. 새 모드 CSV는 `spawn_pipeline=BatchedPooled_v1`로 식별한다. 메모리 보관량도 함께 비교해야 한다.

이 기능은 **테스트용 선택 모드**이며 실제 게임의 네트워크 입력을 요청 버퍼로 전환하거나 새 생명주기를 기본 활성화하지 않는다. 일반 게임 경로를 공유하는 최소 변경은 공통 스폰 함수, 선택 가능한 Entity 재사용 경로, 풀 사전 준비/제거 훅이다. 새 매니저는 추가하지 않았다.

`Tools > Performance Tests > Open ECSComparisonTestScene`으로 새 비교 씬을 연다. 기존 `PerformanceTestScene`, 게임 씬, Build Settings는 변경하지 않는다.

## 자동 측정 / CSV

1. 비교 씬만 열고 `TEST_PerformanceSpawner`의 **Auto Spawn을 끈다**. Ingredient ID, Use Conveyor, 배치와 간격을 설정한다.
2. `TEST_ECSComparison`의 CSV Counts(기본 50/100/200/500), CSV Repetitions(기본 1), CSV Sample Frames(안정 단계마다 기본 120프레임), CSV Settle Seconds(기본 2초)를 설정한다. 0도 입력할 수 있다. **CSV Pool Preparation**은 `Cold`(기본, 예열 없음) 또는 `Prewarmed`(해당 수량 미리 준비)를 선택한다.
3. Play 전에 **Run BOTH modes and save CSV**를 누른다. HybridECS와 GameObjectOnly를 각각 새 Play 세션에서 실행한 뒤 종료한다. Backend와 자동 시작 설정은 원래 값으로 복원하며 씬/Build Settings를 저장하지 않는다. 실행 중 Play를 직접 종료하거나 Stop CSV를 누르면 나머지 자동 실행도 중단한다.
4. 완료 후 **Open last CSV folder** 버튼 또는 `Tools > Performance Tests > Open last comparison CSV folder`로 결과를 연다. Console에도 `Both modes completed. CSV folder:` 경로가 출력된다. 한 모드만 측정하려면 직접 Play 후 **Run this mode and save CSV**를 누른다. CSV Start On Ready는 Player에서 자동 실행할 때 사용할 수 있다.

저장 위치: `Application.persistentDataPath/PerformanceTests/ECSComparison/<실행ID>/<모드>/`. 두 모드는 같은 실행ID 아래에 저장한다. 기존 결과와 이름이 겹치면 별도 폴더를 만든다. Windows 실제 경로는 Console에 출력된다.

- `summary.csv`: **먼저 볼 파일**. 수량/반복/단계별 한 행. 시간 단위 ms, 메모리 단위 bytes. `_mean` 평균, `_p95` 95% 지점, `_max` 최댓값, `_sum` 구간 합계, `_last` 구간 마지막 유효 값.
- `frames.csv`: 프레임별 원본. 단계, 수량, 실제 검증된 GameObject/Entity/재사용 개수와 모든 카운터를 기록한다.
- `settings.csv`: Unity/Editor/Batch/그래픽 장치/해상도/VSync/목표 FPS/재료 배치 설정과 각 열의 실제 Profiler Marker 이름. 지원하지 않는 카운터는 **빈칸**이며 UNAVAILABLE로 명시한다.

| CSV 단계 | 측정 내용 |
|---|---|
| Prewarm | Prewarmed에서만 기록. 해당 수량의 GameObject/컴포넌트 캐시를 준비하는 비용. Entity는 생성하지 않음 |
| Baseline | 활성 재료 0개. Cold는 빈 풀, Prewarmed는 해당 수량을 풀에 보관한 상태 |
| FirstSpawn | 첫 요청 제출부터 생성 검증 완료 + 후속 1프레임까지 |
| Spawned | 안정화 대기 후 재료를 유지하는 상태 |
| Return | 풀 반환부터 기존 ECS 정리/반환 완료 + 후속 1프레임까지 |
| Returned | 안정화 대기 후 Pool에 보관된 상태 |
| ReuseSpawn | 같은 수량 재사용 제출부터 검증 완료 + 후속 1프레임까지 |
| Reused | 안정화 대기 후 재사용한 재료를 유지하는 상태 |

각 수량/반복은 해당 재료 Pool을 비운 뒤 시작한다. Prewarmed에서는 CSV 수량만큼 준비하며 스포너의 수동 Prewarm Count는 사용하지 않는다. ResourceManager로 로드한 에셋, ECS의 이미 확보한 내부 메모리와 이전 수량의 캐시는 유지된다. 완전히 새로운 프로세스/에셋 로딩의 최초 비용을 측정하는 테스트는 아니다. 기본 500개까지 추적 버퍼는 기존 스포너가 미리 준비한다.

### GameObject 사전 준비 최적화 전후 비교

1. 이전 CSV(`20261019_065005_360_cffa06e2`)는 Cold 기준이다. 동일 Ingredient/Conveyor/배치/수량/반복/프레임/대기 시간/Editor·Player 조건으로 **Cold**를 실행하면 코드 변경 전후를 비교할 수 있다. 이전 실행과 하드웨어·Editor 상태 차이가 있을 수 있으므로 반복 중앙값을 사용한다.
2. 같은 설정으로 **Prewarmed**를 실행하면 생성 작업을 사전 준비로 옮긴 효과를 확인한다. 두 실행은 별도 실행ID 폴더에 저장된다. 우선 HybridECS끼리 비교하고, GO와 비교할 때는 Pool Preparation도 같게 맞춘다.
3. `FirstSpawn`과 `ReuseSpawn`: `submit_ms_sum + spawn_process_ms_sum`(Hybrid), `pool_spawn_ms_sum`, `gc_alloc_bytes_sum`, `frame_interval_ms_max`를 비교한다. 부모 처리 시간에 포함된 pool/entity 시간을 다시 더하지 않는다.
4. 새 `pool_create_ms_sum`은 풀 부족으로 새 인스턴스를 만드는 비용이다. Prewarmed의 FirstSpawn/ReuseSpawn에서는 **0**이어야 한다. `Prewarm`의 `prewarm_ms_sum`, GC, 프레임 최대 시간도 함께 본다. 비용을 제거한 것이 아니라 준비 시점으로 옮긴 것이다.
5. `Baseline`의 `total_allocated_bytes_last`, `gc_used_bytes_last`, `object_count_last`로 예열의 보관 메모리를 비교한다. Prewarmed는 이미 준비된 객체를 포함하므로 Spawned−Baseline만으로 Cold와 비교하면 예열 메모리가 빠진다. 전체 메모리는 Editor 영향도 포함한다.

`settings.csv`의 `spawn_preparation_version=PrewarmAndNonAllocReset_v1`, `pool_preparation=Cold/Prewarmed`로 결과를 구분한다. 기존 CSV에는 이 키가 없다. 기존 열·단계 이름은 유지하며 새 marker 열과 Prewarm 단계만 추가했다. 이 변경은 요청/Entity 생성·파괴 정책이나 NetworkID를 바꾸지 않는다. 컴포넌트 캐시는 풀 인스턴스마다 메모리를 보관하며 ClearPool에서 함께 해제한다. 실제 게임의 예열 수량·로딩 시점은 기존 `PrewarmPool(prefabKey, count)` 호출자가 정한다. 스폰 중 자동 예열이나 임의의 대기·프레임 분산은 추가하지 않는다.

비교할 대표 열: FirstSpawn/ReuseSpawn의 `submit_ms_sum`, Hybrid의 `spawn_process_ms_sum` 또는 GO의 `go_spawn_ms_sum`, `gc_alloc_bytes_sum`; Spawned/Reused의 `main_thread_ms_mean/p95`, `script_update_ms_mean`, `physics_simulate_ms_mean`, `conveyor_ms_mean`; 각 안정 단계의 `gc_used_bytes_last`, `total_allocated_bytes_last`, `object_count_last`, `mesh_bytes_last`. GC 할당은 각 프레임의 전체 관리 할당이므로 테스트 상태 변경/기존 검증/로그와 Editor 할당도 포함될 수 있다.

Main Thread/PlayerLoop와 `frame_interval_ms`에는 대기/Editor 비용도 포함된다. Script 열은 이름에 적힌 Update/LateUpdate/FixedUpdate 콜백의 시간이며 Profiler의 Scripts 색상 전체 합계가 아니다. Timing Marker는 메인 스레드만 수집하고 부모/자식 시간이 중복될 수 있으므로 합산하지 않는다. `gc_incremental_ms`는 Incremental GC Marker만 측정하며 모든 GC 시간을 대표하지 않는다. Recorder는 계속 실행하고 프레임 합산이 끝난 직전 프레임 값을 읽는다. 지원되는 Marker가 그 프레임에 호출되지 않은 경우는 0이며, 단발 생성 Marker가 유지 구간에 반복 기록되지 않는 것을 실행 검증한다.

측정 버퍼와 메타데이터는 Baseline 전에 준비하고 프레임 수집에서는 문자열/파일/컬렉션 추가를 사용하지 않는다. CSV 생성·정렬·쓰기와 완료 로그는 모든 Recorder를 Dispose한 뒤 수행한다. 강제 GC는 하지 않는다. 취소/오류/Play 종료 시 확보한 데이터가 있으면 부분 CSV를 남기고 run_status를 Completed와 구분한다. 결과 비교에는 같은 환경의 Completed 행을 사용한다. Deep Profile은 끄고, 최종 수치는 동일 Development Player 조건에서 비교한다.

## 사용 순서

1. Play 전에 `TEST_ECSComparison` 오브젝트의 `Backend`를 `HybridECS` 또는 `GameObjectOnly`로 설정한다. 기본은 HybridECS, Auto Spawn은 꺼짐이다.
2. 동일 오브젝트의 기존 `TEST_PerformanceSpawner`에서 같은 Ingredient ID, 개수, 배치, Prewarm Count를 사용한다. 기본은 감자 10300, 바닥 Grid, 예열 0이다. 0/50/100/200/500개를 선택할 수 있다.
3. Play 후 Ready를 기다리고 Zero 또는 Spawn 버튼을 누른다. Inspector에서 실제 backend, 단계, 활성 GameObject 수, 재료 Entity 수를 확인한다.
4. `Return all ingredients to pool` 후 Returned를 기다린다. `Respawn returned batch`로 같은 재료/수량의 Pool 재사용을 비교한다.
5. 다른 backend는 반드시 Play를 종료한 뒤 새 세션으로 시작한다. 실행 중에는 backend 선택이 잠기며 Pool을 반환해도 변경할 수 없다.

두 모드의 초기 입력, 카메라, Collider/Rigidbody/재료 특성, 기존 ObjectPoolManager/ResourceManager/DataManager/Router, 벨트와 순환 보조 코드는 공통이다. 초기 난수 seed도 동일하게 고정하고 비교 컴포넌트 종료 시 이전 난수 상태를 복구한다. 수량별 Cold 비교는 새 Play 세션으로 시작하거나 해당 재료 Pool을 비우고 다음 프레임 이후 실행한다. Warm 비교는 동일하게 예열하거나 반환된 Pool을 재사용한다.

## 비교 범위

| 항목 | HybridECS | GameObjectOnly |
|---|---|---|
| 스폰 입력 | 기존 IngredientSpawnRequest Entity | 같은 데이터 형식의 테스트용 관리 배열. ECS Entity를 만들지 않음 |
| 실제 스폰 | 기존 IngredientSpawnSystem | 테스트 스포너의 GameObject 전용 처리 |
| GameObject 생성 | 기존 ObjectPoolManager.Pop | 동일 Pop. 테스트 코드에서 프리팹 직접 Instantiate 없음 |
| 데이터/등록 | 기존 초기화·Router·활성 목록·컨베이어 등록 | 동일한 재료 초기화·등록 순서를 테스트 코드에서 실행 |
| 재료 Entity | 재료 수만큼 생성 | 0개 |
| 기본 ECS World | 기존 World 유지 | 측정 전 PlayerLoop에서 제거하고 Dispose |
| 반환 | 기존 Pool.Push + ECS 삭제 요청/정리 시스템 | 같은 Pool.Push. 기본 World가 없어 삭제 요청 Entity도 생성되지 않음 |
| 유지 | 기존 GameObject와 ECS 시스템 실행 | 같은 GameObject 동작, 기본 ECS 시스템 실행 없음 |

현재 게임의 재료 Entity 생성/성분 기록/정리와 기본 ECS World 실행을 포함한 **현재 구성 전체의 차이**를 측정한다. 컨베이어 이동을 ECS로 새로 구현하거나 동일 연산의 Burst 효과를 측정하는 씬은 아니다. 기본 World 초기화/제거 자체는 Ready 전 준비 구간이므로 일반 스폰 구간과 분리한다. ECS 패키지와 타입 정보까지 프로젝트에서 제거하는 것은 아니며, GameObjectOnly에서도 이미 로드된 패키지 코드/일부 정적 데이터는 남을 수 있다.

GameObject 전용 경로는 기존 Spawn System의 **재료** 경로만 복제한다. 생산 코드가 재료 초기화/등록 순서를 변경하면 이 테스트 경로도 맞춰 갱신해야 한다. 서버 패킷, 요리 결과물, 플레이어 소유권에 따른 파괴/교체는 에뮬레이션하지 않는다.

컨베이어를 켤 경우 두 모드 모두 기존 MonoBehaviour 컨베이어를 사용한다. 순환 재등록은 기존 `TEST.Performance.ConveyorLoop.*`에 포함되므로 동일 조건으로 비교한다. 스폰 시스템이나 성분 계산이 Burst로 빨라진다는 의미로 해석하지 않는다.

## Profiler

- 생성 입력: 두 모드 공통 `TEST.Comparison.SubmitBatch`. Hybrid는 ECS 요청 Entity를 생성하고 GameObjectOnly는 같은 스폰 정보를 관리 배열에 기록한다. 배열 용량과 테스트 추적 객체는 양쪽 모두 초기화 때 준비한다.
- 생성 처리: Hybrid의 `Ingredient.SpawnRequest.Process` 및 Entity `RecordCommands/Playback`과, GameObjectOnly의 `TEST.Comparison.GameObjectOnly.Spawn`을 비교한다. 한 배치의 제출/처리는 다른 프레임에 표시될 수도 있다.
- Pool: 양쪽 모두 `Ingredient.ObjectPool.Spawn`을 사용한다.
- 반환: 양쪽 공통 `TEST.Performance.Clear`와, Hybrid의 `IngredientEntityCleanupSystem` 실행 프레임을 함께 확인한다. GameObjectOnly에는 기본 World와 ECS 요청 자체가 없어야 한다.
- 유지: Ready/스폰 검증/물리 안정화 이후 같은 길이의 구간에서 CPU Frame Time, Scripts, Physics, GC Alloc을 비교한다. EditorLoop와 대기 시간을 재료 계산 시간으로 합산하지 않는다.
- 메모리: 기존 Profiler의 `Ingredient Memory` 모듈에서 GC Allocated In Frame, GC Used Memory, TEST.Memory.Total Allocated Memory, Object Count, Mesh Memory를 비교한다. Pool 반환 후에도 비활성 GameObject와 로딩된 에셋은 보존된다.

최종 비교는 동일한 Development Player, 해상도, VSync/목표 FPS, 재료 수, Pool 상태와 배치에서 수행한다. 기존 게임의 Build Settings를 수정하지 말고 별도 Build Profile에서 이 씬만 사용한다. CPU Marker의 부모·자식 시간을 중복 합산하지 않는다.

## 검증

`TEST_ECSComparisonValidation.RunBatchValidation`은 backend별 별도 Unity 호출로 실행한다. 열린 동일 프로젝트의 Editor와 동시에 실행하지 않는다. `-quit`은 붙이지 않으며 검증이 종료 코드를 반환한다.

```text
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod TEST_ECSComparisonValidation.RunBatchValidation -ecsComparisonMode HybridECS -logFile <hybrid-log>
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod TEST_ECSComparisonValidation.RunBatchValidation -ecsComparisonMode GameObjectOnly -logFile <gameobject-log>
```

각 호출은 바닥/컨베이어 × 0/50/100/200/500의 10개 조건에서 생성·반환·재사용, 실제 GameObject 재사용, 새 NetworkID, Router/데이터/벨트 등록, 컨베이어 순환을 검사한다. Hybrid는 재료 Entity 수와 삭제 요청 정리를, GameObjectOnly는 생성/반환/재사용 이후에도 기본 World가 없는 것을 검사한다. 기존 성능 테스트 회귀 검증은 `TEST_PerformanceValidation.RunBatchValidation`로 별도 수행한다. Headless 검증 결과는 실제 렌더링 성능 수치가 아니다.

CSV 검증은 `TEST_ECSComparisonCsvValidation.RunBatchValidation`에 동일한 `-ecsComparisonMode` 인자를 사용한다. 0/50/100/200/500 × 2회 반복, 컨베이어, 프레임/단계별 실제 수량, 단발 Marker 비반복, CPU/메모리 카운터, GC 합계, 재실행 시 파일 보존과 취소 시 부분 저장을 확인한다. `TEST_ECSComparisonCsvValidation.RunBothBatchValidation`은 실제 자동 실행 버튼의 두 Play 세션과 Inspector 설정 복원을 검사한다. 두 진입점도 `-quit`을 붙이지 않는다.
