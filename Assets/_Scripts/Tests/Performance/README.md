# PerformanceTestScene

## 씬 설정

- `Tools > Performance Tests > Open PerformanceTestScene` 또는 같은 폴더의 씬을 연다.
- 다른 씬을 함께 로드하지 않고 이 씬에서 새 Play 세션을 시작한다.
- `TEST_Performance` 오브젝트에서 `TEST_PerformanceSpawner`를 설정한다.
- DataManager는 기존 `Resources/Json` 모드를 사용하고 ResourceManager는 기존 `Init()`으로 Addressables를 로드한다.
- GameManager, ServerManager, IngredientNetworkBridge는 배치하지 않는다.
- ObjectNetworkRouter는 **GameObject는 활성, 컴포넌트는 비활성**으로 유지한다. 기존 `Add/Remove` 등록 메서드는 사용하고 서버 구독은 시작하지 않는다.
- 초기화가 실패하면 Console/Inspector에 오류를 표시하고 스폰하지 않는다. 서버 연결이나 대체 데이터로 우회하지 않는다.
- 기존 `ConveyorSpawnTimingDebug`는 이 테스트가 실행되는 동안에만 비활성화하고 종료 시 원래 상태로 복구한다.
- 서버 의존 파괴·교체·플레이어 소유권 동작을 에뮬레이션하지 않는다. 재료 Trait, Rigidbody, Collider 설정은 기존 프리팹을 그대로 사용한다.
- 기본 씬에 바닥과 기존 ConveyorPath/ConveyorBeltController를 사용하는 120m 길이의 레인 10개가 들어 있다. 레인 수에 따라 컨베이어 순회 비용이 달라지므로 비교 시 같은 씬 구성을 유지한다.
- 기존 게임의 Build Settings는 변경하지 않는다. Player 측정 시 별도 Build Profile에서 이 씬만 포함하고 Development Build 및 Autoconnect Profiler를 사용한다.

## Inspector

| 설정 | 기본값 / 동작 |
|---|---|
| Ingredient Id | `10300` 감자. 기존 Ingredient/IngredientStat 및 로딩된 CatchableObj 프리팹이 있어야 함 |
| Spawn Count | `50`. Memory 프리셋 0 / 50 / 100 / 200, 기존 Stress 프리셋 50 / 100 / 300 / 500 제공 |
| Auto Spawn | 켜짐. 모든 초기화가 완료되면 한 번 생성 |
| Prewarm Count | `0` = 초기 예열 없음. 자동 스폰 전 또는 Prewarm 버튼으로 지정 개수를 **추가** 예열 |
| Start Position | `(-48, 1, -20)` 월드 좌표 |
| Spacing | `(2, 0, 2)`. Grid는 X/Z 간격, Line은 이 벡터 전체를 사용 |
| Use Grid / Grid Columns | 켜짐 / `25`. 끄면 Line 배치 |
| Use Conveyor | 꺼짐. 켜면 Floor 배치 설정 대신 레인별 경로상 배치를 사용 |
| Conveyor Belts | 기본 10개 레인. 요청이 레인에 순서대로 분배됨. 고유한 양수 Belt ID 필요 |
| Conveyor Start Distance / Spacing | `2 / 2`m. 각 레인에서의 시작 거리와 재료 간 거리 |

NetworkID는 `1,000,000,000`부터 재료마다 증가한다. Clear 후에도 같은 세션에서는 재사용하지 않는다. 기존 GameObject/Router/Entity/요청/벨트 등록과 중복되면 배치 생성을 중단한다. `ConveyId=0`은 바닥 스폰용으로 비워 둔다.

## Profiler 항목

Deep Profile을 끄고 CPU Usage의 Timeline/Hierarchy, Physics, Memory 모듈을 기록한다. Editor에서는 Scene 뷰 Gizmos와 Inspector 갱신 비용이 있으므로 최종 비교는 동일한 Development Player 조건에서 진행한다.

| Marker | 측정 범위 |
|---|---|
| `TEST.Performance.EnqueueSpawnRequests` | 한 프레임의 여러 IngredientSpawnRequest 생성. 테스트 관리 목록 할당과 사전 검증은 이 범위 밖 |
| `Ingredient.SpawnRequest.Process` | 기존 Spawn System 업데이트. Pool, 등록, Entity 명령 및 Playback 포함. 요청 없는 프레임에도 기존 시스템의 기본 비용이 존재 |
| `Ingredient.ObjectPool.Spawn` | 기존 위치/회전 지정 Pop 오버로드. Pool 재사용 또는 부족한 오브젝트 생성 포함 |
| `Ingredient.Entity.RecordCommands` | 재료 Entity/컴포넌트 생성 명령 기록. 실제 Entity 생성 시간과 구분 |
| `Ingredient.Entity.Playback` | 실제 ECB 실행. Entity 생성/컴포넌트 적용 및 요청 Entity 삭제 포함 |
| `Ingredient.Conveyor.Update` | 각 기존 벨트의 전체 Update. Hierarchy 합산으로 전체 레인 비용 확인 |
| `TEST.Performance.ConveyorLoop.Check` | 테스트 보조 코드의 순환 검사. 레인 이동 비용과 별도 확인 |
| `TEST.Performance.ConveyorLoop.ReRegister` | 끝에 도달한 재료를 시작으로 재등록하는 테스트 보조 비용. 기존 등록 코드의 ConveyorItemState 할당 포함 |
| `TEST.Performance.VerifySpawn` | 생성 직후 1회 GameObject/Entity 개수 검증. 지속 측정 구간에서 제외 |
| `TEST.Performance.Prewarm` / `TEST.Performance.Clear` | 측정 전 준비/반환 비용. 스폰 및 지속 측정 구간과 구분 |

요청 제출과 기존 ECS 시스템의 처리는 PlayerLoop 순서에 따라 같은 프레임 또는 다음 프레임에 나타날 수 있으므로 두 프레임을 함께 확인한다.

## 50 / 100 / 300 / 500개 테스트

1. 수동 비교에는 Play 전에 `Auto Spawn`을 끈다. Play 후 `Ready`를 기다린다.
2. `Spawn 50`, `Spawn 100`, `Spawn 300`, `Spawn 500` 중 하나를 누른다. 모두 한 프레임에 해당 수의 요청을 제출한다.
3. Spawn Stress Test는 요청 제출 및 Spawn System 처리 프레임의 CPU Spike, GC Alloc, Pool/ECB 비용을 기록한다.
4. Runtime Stress Test는 `Running: N GameObjects / N Entities` 확인 후 초기 물리 안정화가 지난 구간에서 Frame Time, Scripts, Physics, GC Alloc, Memory와 컨베이어 비용을 기록한다. 매 프레임 생성/검증 로그는 발생하지 않는다.
5. 다음 개수는 `Clear batch`를 누르고 `Cleared / Ready`가 된 뒤 실행한다. 기존 Pool.Push 및 IngredientEntityCleanupSystem으로 정리한다. Clear는 Pool을 보존한다.
6. Cold Spawn은 매번 새 Play 세션 또는 Clear 후 `Empty ingredient pool`을 누르고 다음 프레임 이후 스폰한다. Warm Spawn은 Prewarm Count를 목표 개수로 설정한 후 Prewarm을 누르거나 Clear로 반환된 Pool을 재사용한다. 예열은 누를 때마다 추가되므로 누적 메모리 측정에 주의한다.
7. 바닥 측정 후 Clear하고 `Use Conveyor`를 켜 같은 개수를 비교한다. 기본 10레인에서는 500개가 각 레인에 50개씩 배치된다. 재료는 삭제·재스폰하지 않고 동일 NetworkID/GameObject/Entity를 유지하며 순환한다. 순환 재등록 GC는 별도 Marker에서 확인한다.

## Memory / Pool 재사용 비교

기존 씬을 다시 만들 필요 없이 `TEST_Performance`의 Inspector에 Memory 테스트 버튼이 추가된다. 각 단계는 다음 버튼을 누를 때까지 유지되므로 Memory Profiler 스냅샷을 수동으로 찍거나 동일 길이의 안정 구간을 비교할 수 있다. 패키지 설치, 자동 스냅샷, 강제 GC, Resources/Addressables 해제는 수행하지 않는다.

1. Play 전에 `Auto Spawn`을 끄고 `Prewarm Count = 0`으로 설정한다. 재료 수에 따른 기본 비교에는 `Use Conveyor`를 끄고, 컨베이어 비교 시에는 모든 단계에서 같은 설정을 유지한다.
2. 새 Play 세션에서 Ready를 기다린 뒤 `Zero (0)`를 누른다. 요청 Entity나 재료를 생성하지 않는다. 초기화된 데이터·에셋·테스트 관리 버퍼가 포함된 기준 상태이다.
3. Memory 버튼의 `Spawn 50 / 100 / 200` 중 하나를 누른다. `Spawned`와 GameObject/Entity 수를 확인한 후 안정 구간을 기록한다.
4. `Return all ingredients to pool`을 누른다. `Returning`에서 기존 ECS 정리를 기다린 뒤 `Returned`가 된다. 활성 수는 0, `Last returned count`는 반환 수이다. 반환 직후의 활성 수 0은 Pool이 비어 있는 첫 기준 상태와 다르다.
5. `Respawn returned batch`를 누른다. 마지막으로 반환한 **동일 Ingredient ID와 실제 반환 수**로 기존 SpawnRequest 경로를 다시 사용한다. `Reused`와 `Verified reused GameObjects = N`을 확인한다. FIFO Pool의 다른 기존 객체가 나올 수도 있으므로, 재스폰 직전에 Pool에 비활성 상태로 존재했던 객체 ID 전체와 대조한다. NetworkID와 ECS Entity는 기존 스폰 구조에 따라 새로 발급·생성한다.
6. 수량별 최초 생성 비교는 새 Play 세션에서 반복한다. 같은 세션에서는 Pool이 이전 최대 수량만큼 남아 있을 수 있다. `Empty ingredient pool`로 해당 재료의 비활성 Pool만 비운 뒤에도 Addressables 등은 유지된다.

Inspector에는 단계, 요청 수, 검증된 활성 재료/Entity 수, 마지막 반환 수, 검증된 GameObject 재사용 수가 표시된다. Inspector는 단계 전환 시에만 추가 갱신하며, 매 프레임 문자열·로그·배열을 만들지 않는다. 테스트 추적용 객체 500개와 버퍼를 0개 기준 측정 전에 준비하고 이후 재사용한다. 반환 시 테스트가 잡고 있던 재료와 컨베이어 상태 참조도 비운다.

Profiler 창의 `Profiler Modules`에서 **Ingredient Memory**를 켜면 다음 다섯 카운터가 한 모듈에 표시된다. 프레임 선택 시 아래 상세 값으로 비교한다.

| 항목 | 비교 목적 |
|---|---|
| `GC Allocated In Frame` | 생성·반환·재스폰 프레임의 관리 힙 할당량. GC 정리 시간이나 스냅샷의 잔존 메모리와 구분 |
| `GC Used Memory` | 현재 사용 중인 관리 힙. 반환 후에도 GC 실행 전까지 바로 줄지 않을 수 있음 |
| `TEST.Memory.Total Allocated Memory` | `Profiler.GetTotalAllocatedMemoryLong()`으로 얻은 Unity 내부 할당자의 사용 바이트. OS 프로세스 전체 메모리나 Reserved Memory가 아님 |
| `Object Count` | 활성·비활성을 포함한 Unity 오브젝트 수. Pool 반환만으로는 객체가 파괴되지 않으므로 수가 유지될 수 있음 |
| `Mesh Memory` | 로드된 Mesh 메모리. 기존 공유 Mesh와 에셋 선로딩 때문에 재료 수에 비례해 늘지 않을 수 있음 |

네 개의 Unity 기본 카운터를 그대로 사용하고, Total Allocated Memory만 테스트 스포너의 네이티브 카운터로 기록한다. 이 카운터 갱신은 관리 힙 할당 없이 실행되며 Development Player에서도 사용할 수 있다. 프로파일러 모듈은 Editor 표시용이며 새 런타임 매니저를 추가하지 않는다.

CPU Timeline에는 `TEST.Memory.Zero`, `Spawned`, `Returned`, `Reused` 전환 Marker가 기록된다. 버튼/검증 직후의 준비 할당과 스냅샷 캡처 프레임은 지속 측정 구간에서 제외한다. 메모리 비교는 해당 단계에 머문 안정 구간에서, GC Alloc은 작업 발생 프레임에서 각각 확인한다. 컨베이어 사용 시 기존 `TEST.Performance.ConveyorLoop.ReRegister` 비용은 별도이다.

참고: [Unity Memory 모듈](https://docs.unity3d.com/6000.3/Documentation/Manual/ProfilerMemory.html), [Total Allocated Memory API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Profiling.Profiler.GetTotalAllocatedMemoryLong.html).

배치 통합 검증 진입점은 `TEST_PerformanceValidation.RunBatchValidation`이다. Unity `-batchmode -nographics -projectPath <project> -executeMethod TEST_PerformanceValidation.RunBatchValidation -logFile <log>`로 호출하며 `-quit`은 붙이지 않는다. 기존 바닥/컨베이어 × Cold/Warm × 네 개수의 16배치와 신규 0/50/100/200 및 FIFO 재사용 5시나리오, 다섯 Profiler 카운터를 검증하고 종료한다. 이 검증은 실제 렌더링 성능 수치를 측정하지 않는다.
