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
- 중도 입장한 플레이어의 초기 페이지는 서버에서 현재 번호를 `S_StageSet`으로 보내야 동기화됩니다.

## TEST_SetStage의 Tab 테스트

`TEST_SetStage`는 StageManager가 있는 씬에서 자동 생성됩니다. 스테이지 번호 입력, 변경 버튼, F1 기능은 제거했으며, 실린더로 선택한 현재 스테이지를 **Tab**으로 즉시 시작합니다. 현재 스테이지·남은 시간 표시와 4분 후 테스트 복귀 처리는 유지합니다. 스테이지 선택은 World Canvas에서 합니다.

Tab은 기존 문을 비활성화하는 테스트용 시작입니다. 정식 플레이의 전원 진입 조건을 확인할 때는 사용하지 않습니다. `StageTestKeys`도 같은 씬에 붙어 있으면 그 스크립트의 별도 Tab 처리(단계 건너뛰기)도 실행되므로 함께 쓰지 않도록 확인합니다.

Tab 테스트까지 제거하려면 Unity Project 창에서 `TEST_SetStage.cs`를 삭제하면 됩니다. 다른 스테이지 UI는 이 클래스에 의존하지 않습니다. 씬/프리팹에 직접 붙여두었다면 해당 컴포넌트도 제거하세요.
