# 스테이지 UI 적용 방법

## 빠른 적용

1. 기존 `ServerManager`, `PlayerSpawnManager`, `DataManager`, `StageManager`가 있는 씬에서 `GameObject > UI > Stage Selection World Canvas`를 실행합니다. 보드와 UI, 양쪽 실린더가 연결된 상태로 생성됩니다.
2. 생성된 `Stage Selection Board`의 위치·회전·크기를 안내판에 맞춥니다. TMP 텍스트에는 한글을 지원하는 폰트를 지정합니다.
3. `GSpreadReader`의 Sheets에 **Class Name = `Stage`**와 새 스테이지 시트의 `sheetId`를 추가합니다. 필드명은 `id`, `stageNum`, `goal`, `tip`이며 타입은 순서대로 `int`, `int`, `int`, `string`입니다. `IngredientGroup` 시트도 등록합니다. JSON 모드에서는 각각 `Assets/Resources/Json/Stage.json`, `IngredientGroup.json`을 `{ "list": [...] }` 형식으로 준비합니다.
4. 보드의 `StageSelectionController`에서 View가 생성된 UI를 가리키는지 확인합니다. 스테이지 번호·목표·팁은 `DataManager`가 로드한 `Stage` 테이블에서 읽으므로 별도 에셋 연결은 필요 없습니다. 기존 보드와 실린더 연결은 그대로 사용할 수 있습니다. 방에 입장한 뒤 양쪽 실린더로 페이지 변경을 확인합니다.

예: `Stage`의 `{ "id": 101, "stageNum": 1, "goal": 300, "tip": "재료를 준비하세요." }`는 **스테이지 1**, 목표 **300**과 해당 팁으로 표시됩니다. 재료는 `IngredientGroup.difficulty = 1`인 행을 사용하며 서버에도 번호 **1**을 보냅니다. `id = 101`은 데이터 조회 키입니다.

`StageSelectionSession`은 실행 시 자동 생성됩니다. 직접 씬에 붙일 필요가 없습니다. 재료 아이콘은 Addressables의 Sprite 주소를 사용합니다. 이벤트 칸은 연결을 유지하되, 현재 `Stage` 테이블에 이벤트 필드가 없으므로 비워둡니다.

메뉴가 보이지 않으면 Unity 컴파일이 끝났는지 Console을 확인합니다. `StageSelectionController`, `StagePageCylinder`, `StageBoardItemUI`를 찾을 수 없다는 오류가 있으면 Project 창에서 `Assets/_Scripts` 폴더를 우클릭하여 **Reimport**합니다. 메뉴는 에디터 스크립트 컴파일이 성공한 뒤 등록됩니다.

## 이미 만든 Canvas와 실린더에 연결하기

Canvas의 Render Mode를 **World Space**로 설정하고 아래 컴포넌트를 붙여 Inspector에서 연결합니다.

| 컴포넌트 | 연결할 항목 |
| --- | --- |
| `StageSelectionController` | View → `StageSelectionWorldUI` |
| `StageSelectionWorldUI` | Stage Title, Tip Text, Goal Text, Status Text, Previous Arrow, Next Arrow에 각각 TMP 텍스트 연결 |
| `StageBoardItemUI` | 재료·이벤트 각 칸에 부착하고 Icon → Image, Title → 이름 TMP, Description → 태그/설명 TMP 연결 |
| `StagePageCylinder` | 각 실린더에 Collider와 함께 부착하고 Board → `StageSelectionController`, 왼쪽 Direction → Previous, 오른쪽 → Next |

`StageSelectionWorldUI`의 Ingredients 배열은 **왼쪽 위→아래 3칸, 오른쪽 위→아래 3칸** 순서로 연결합니다. Events 배열은 **위→아래 3칸**입니다.

실린더의 Layer는 플레이어의 `InteractLayerMask`에 포함해야 공격을 받습니다. 기본 생성 실린더는 10번 레이어입니다. 몸 충돌도 받으려면 플레이어와 실린더 사이의 Physics 충돌이 허용되고, 한쪽에 Rigidbody가 있어야 합니다. Collider의 Is Trigger를 켜도 진입으로 페이지를 넘깁니다.

## 동작 기준

- `IngredientGroup` 필드: `grooviroom`, `difficulty`, `ingredient1`, `beltID` (모두 int).
- 페이지는 `Stage.stageNum`의 오름차순입니다. 번호는 양수여야 하며 중복 번호는 첫 행만 사용하고 경고합니다. 재료가 없어도 페이지는 표시되며, 스테이지 테이블에 없는 재료 그룹은 페이지를 만들지 않습니다. 첫·마지막 페이지에서는 멈춥니다.
- 서버의 기본 선택 번호가 1이므로 `stageNum = 1`인 행을 포함합니다. 서버가 선택한 번호가 테이블에 없으면 누락 안내를 표시합니다.
- 재료는 `IngredientGroup.difficulty = Stage.stageNum`으로 매핑하고 중복을 제거하여 최대 6개 표시합니다. 팁은 테이블의 `tip`, 목표는 `goal` 숫자를 그대로 표시합니다. 목표에 별도의 단위는 붙이지 않습니다.
- 몸 충돌·공격 모두 페이지를 넘깁니다. 서버가 `S_StageSet`으로 확인하면 모든 플레이어의 표시를 갱신합니다.
- 주방 시작은 기존 서버의 전원 진입 조건을 사용합니다. 보드는 시작 패킷을 보내지 않으며 진행 중 페이지 변경을 잠급니다.
- 새 플레이어가 입장하면 기존 플레이어가 `S_PlayerEnter` 알림을 받아 현재 선택을 한 번 재전송하고, 기존 서버의 `S_StageSet` 방송으로 후입장자의 페이지도 맞춥니다. 진행 중인 페이지 변경이 있으면 그 응답을 받은 뒤 최신 번호를 재전송합니다. 후입장자는 동기화가 완료되기 전까지 스테이지 표시·실린더 변경을 대기합니다. 별도 안내 문구는 표시하지 않습니다.
- 이 입장 동기화는 스테이지 시작 전 선택 화면에 적용됩니다. 진행 중에는 재전송하지 않아 서버의 재료 스폰 설정을 다시 적용하지 않습니다.

## 실사용 HUD와 공동 재화

HUD는 `StageHudUI : UIBase`입니다. UI를 만드는 런타임 코드는 없으며 `Assets/_Scripts/Resources/UI/StageHudUI.prefab`에 시계 아이콘·남은 시간·Round·획득 황금 콩을 배치했습니다. 방 입장 시에는 로비 HUD가 표시되며, 실제 스테이지 시작 알림을 받으면 `RoomHudPresenter`가 로비 HUD를 숨기고 이 프리팹을 엽니다. 프리팹은 현재 씬 구조에 맞춰 `UICanvas`의 Parents 0번을 사용합니다. 요청한 UI 위치 슬롯이 없거나 비어 있으면 `UIManager`가 등록된 첫 번째 유효 부모를 사용합니다. 부모가 전혀 없으면 `UICanvas Parents` 설정 오류를 명시합니다.

프리팹의 RectTransform과 TMP 텍스트를 편집해 위치·크기·폰트를 변경합니다. HUD는 이 프리팹을 사용하며 테스트용 Canvas는 만들지 않습니다. `TEST_SetStage`는 별도로 유지하여 **Tab → 현재 선택한 스테이지 시작 요청 + KitchenDoor 비활성화**를 처리합니다. StageManager가 있는 초기 씬에서는 자동 생성되며, 다른 씬으로 진입하는 테스트 환경에서는 씬 오브젝트에 직접 붙일 수 있습니다. 시작 확인을 받으면 기존 주방 열림 알림도 전달하므로 HUD 시작 애니메이션과 도구 등록 흐름이 실행됩니다. 로컬 타이머 강제 리스폰은 사용하지 않습니다. `StageTestKeys`에는 별도의 Tab 단계 건너뛰기가 있으므로 함께 붙이지 않습니다.

- **Round:** World Canvas에서 선택한 `Stage.stageNum`을 그대로 표시합니다.
- **시작 애니메이션:** 실제 스테이지 시작 시 시계가 왼쪽에서 먼저 들어오고, 0.2초 뒤부터 남은 시간·Round·재화 영역이 순차적으로 들어옵니다. 프리팹의 `StageHudUI > Stage Start Slide`에서 이동 거리·시간·지연을 조절합니다. 정보 영역 순서는 `Information Panels` 배열로 설정합니다. 게임 시간 배율과 무관하게 재생하며, 중복 시작 알림이나 재화 갱신으로 다시 재생하지 않습니다. 진행 중 HUD를 다시 열면 최종 위치로 표시합니다.
- **Tab 테스트의 HUD 표시:** 주방 문 비활성화와 시작 요청 기능은 유지합니다. HUD는 시작 응답을 받은 뒤 공통 표시 컨트롤러가 전환하므로, 실제 시작 전에 인게임 HUD를 미리 열지 않습니다. 새 스테이지 진입 때는 기존 프리팹을 재사용해도 애니메이션을 재생합니다. 단순히 진행 중 HUD를 다시 여는 경우에는 중복 재생하지 않습니다.
- **남은 시간:** 기존 서버의 4분 제한에 맞춰 `S_OpenDoor(Kitchen)` 또는 `S_StageStart` 수신부터 표시합니다. 두 알림이 모두 와도 카운트다운을 중복 시작하지 않습니다. 0초에서 멈추고, 게임 종료 판단은 `S_StageStop`에 맡깁니다. 현재 패킷에는 서버 종료 시각이 없어 수신 시각 기준 표시입니다.
- **공동 황금 콩:** `RoomHudSession`이 `S_RoomMoney`를 한 번 등록하고 `Money`를 방의 현재 총액으로 대입합니다. 중복 패킷을 받아도 더하지 않으며, HUD가 비활성이었다가 다시 열려도 보관한 최신 값을 표시합니다. 후입장자도 서버의 입장 시 잔액 패킷으로 같은 값을 봅니다.
- 금액은 스테이지 선택·시작·종료로 초기화하지 않습니다. 다른 방에 정상 입장할 때만 이전 방 잔액을 비우고 새 스냅샷을 기다립니다. 소비 요청 패킷과 클라이언트의 임의 가감 처리는 구현하지 않았습니다.

## 결산용 재화 기록

- 실제 스테이지 시작 알림을 받으면 현재 공동 잔액을 `StageStartMoney`로 저장합니다. 페이지 선택만으로는 저장하지 않고, 시작 패킷과 주방 문 알림이 중복되어도 기준 잔액을 다시 잡지 않습니다.
- 플레이 중 `CurrentStageEarnedMoney`는 `현재 잔액 - 시작 잔액`입니다. 재화 패킷마다 보상액을 더하는 방식이 아니므로 중복 패킷으로 획득량이 늘어나지 않습니다.
- 서버의 종료 알림을 받으면 `StageSettlementData`에 `RunNumber`, `StageNumber`, `StartMoney`, `EndMoney`, `EarnedMoney`를 보관합니다. 획득량은 `종료 잔액 - 시작 잔액`이며 로컬 타이머가 0이 되는 것만으로는 결산하지 않습니다.
- 결산 UI는 `RoomHudSession.Instance.State.LatestSettlement`로 최근 기록을 읽거나 `SettlementRecorded` 이벤트를 구독하면 됩니다. `State.Settlements`에는 같은 스테이지 재도전도 별도 기록으로 남습니다. `RunNumber`는 해당 방에서의 클라이언트 실행 순번이며 서버용 플레이 ID가 아닙니다.
- 종료된 기록은 이후 로비 잔액·페이지 선택·다음 스테이지로 변경되지 않습니다. 다른 방에 정상 입장하면 전체 기록을 비웁니다. 파일 저장은 하지 않고 현재 클라이언트 세션 메모리에 보관합니다.
- 시작 전에 잔액 스냅샷을 받지 못했다면 `StartMoney`와 `EarnedMoney`는 `null`로 남겨 잘못된 0원 기준 계산을 방지합니다. 현재 종료 잔액은 종료 알림까지 수신한 마지막 `S_RoomMoney` 값입니다. 종료 알림 뒤에 추가 보상을 보내는 프로토콜이라면 서버의 최종 결과 패킷으로 확정해야 합니다.
