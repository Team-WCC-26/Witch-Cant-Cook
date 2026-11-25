# 스테이지 종료·결산·로비 복귀

1. 클라 타이머 0초: 신규 접수·조작·컨베이어 이동 차단. 그 전에 접수한 큐는 서버 Stop 전까지 전송 가능.
2. S_StageStop (315): 시간 종료 확정. 아직 전송하지 않은 제출 큐도 중단한다. 이미 보낸 요리의 판정·보상과 접시 반환은 계속 수신한다. HUD는 00:00을 유지하며 월드 결산 캔버스는 아직 표시하지 않는다.
3. S_StageResult (317): PendingMoney·Money·IsSuccess로 레시피 보드의 월드 결산 캔버스 표시. 기존 Recipe World Canvas는 숨긴다. 최종 지갑 값을 대입하며 중복 결과로 재지급하지 않는다. 접시 배출 애니메이션 완료를 기다리지 않는다.
4. 각 플레이어가 월드 결산 표시 중 F키를 누르면 C_StageResultConfirm (316)을 한 번 전송한다. 안내 문구는 다른 플레이어 응답 대기 중..으로 바뀌며 F키 중복 입력을 막는다. 통신 예외가 발생하면 F키로 다시 확인할 수 있다. 연결이 없으면 전송 완료 상태로 바꾸지 않는다. 서버는 플레이어별 확인을 중복 없이 처리한다.
5. 서버가 전원 확인 후 S_OpenDoor(Lobby)와 S_LobbyMove (319)를 전송한다. 문 알림은 문 연출만 처리한다. 실제 복귀·초기화는 S_LobbyMove로 시작한다.
6. 로컬 상태 정리 → 참가자별 로비 스폰 → 결산 캔버스 종료 및 레시피 캔버스 복구 → 조작 복구. 다음 Kitchen 문 알림부터 새 판을 시작한다.

## 씬 설정

- 복귀 위치는 PlayerSpawnManager의 기존 spawnRoot(초기 입장 위치)로 통일한다. 별도의 복귀 컴포넌트나 위치 오브젝트는 필요 없다.
- 서버가 주방 도구 ID를 회수하므로 로비 복귀 시 맵 등록 ID도 초기화한다.
- InitialToolSpawnSetup 하나에 칼·팬·접시 위치를 연결한다. 다음 Kitchen 문 알림에서 첫 참가자만 기존 C_ToolSpawn을 전송한다. 실제 생성은 S_ToolSpawn 수신으로 처리한다.
- 맵 고정 도구는 기존 C_ToolRegister/S_ToolRegister 경로로 재등록한다.
- PlayerSpawnManager.TryGetLobbyReturnSpawns가 참가자 1/2를 초기 스폰의 로컬 X -0.75/+0.75 위치에 배치한다. 두 플레이어가 겹치지 않도록 간격만 유지한다. 초기 스폰 위치 자체가 없으면 설정을 기다린다. 중복 로비 이동은 초기화를 반복하지 않는다.

## 정리 범위

주문·피드백 UI, 재료 생성 대기열, 네트워크 오브젝트 풀 반환, 제출 큐·반환 연출, 컨베이어 추적 목록, 조리 도구 상태, 맵 등록 ID, 플레이어 잡기·행동·로켓·체력·입력·속도·원격 보간을 로비 복귀 시 정리한다. 풀 반환은 서버 삭제 응답을 기다리지 않는다.

바닥 오염·이벤트 등 추가 상태는 IClientStageResettable 또는 ClientStageResetHook에 연결한다. 결산 중 재화를 클라에서 다시 더하지 않는다.

## 검증 범위

런타임 컴파일, 종료/결과 분리와 두 번 연속 상태 전이, 중복 결과·확인·이동 방지, 신규 패킷 직렬화를 검사한다. 실제 2인 네트워크 두 판 검증은 별도 실행이 필요하다.
## 월드 결산 보드

- 결산은 Recipe_Big에서만 표시한다. 일반 Recipe에는 결산 프리팹을 두지 않으며 결산 중 RecipeScrollView의 Canvas도 꺼서 비운다.
- StageSettlementWorldCanvas 프리팹은 Assets/Addressables/UIPrefabs에 있고 UI 라벨로 등록되어 있다. MainScene의 Recipe_Big 아래 프리팹 인스턴스를 재사용하며 기존 씬 배치의 위치·크기는 유지한다.
- StageSettlementWorldUI는 활성화된 프리팹 루트에서 Recipe Canvas와 Settlement Canvas를 전환한다. 로비 복귀가 끝나면 결산을 숨기고 레시피 표시를 복구한다.
- 결산 수신 시 각 클라는 Recipe_Big 아래 연결만 확인한다. 가상 플레이어 씬에 결산 인스턴스가 누락된 경우 동일 어드레서블 프리팹을 한 번 생성하고 재사용한다. 일반 Recipe에는 생성하지 않는다.
- 완성된 요리는 이번 판 S_DishState.Success 수신을 집계한다. 표시 큐 처리 전 기록하므로 같은 프레임에 결과가 도착해도 반영한다. 획득 콩/성공 여부는 S_StageResult, 목표는 Stage.goal을 사용한다.
- 칭호/플레이어별 근무 기록은 표시하지 않는다. 캔버스에 버튼·GraphicRaycaster가 없으며 Graphic의 raycastTarget과 Canvas 이벤트 수신을 껐다. F 입력은 StageReturnFlow가 담당한다.
- 현재 서버는 참가자별 확인 알림을 보내지 않는다. 내 확인 전송만 확정 표시하고 상대는 확인 정보 대기로 둔다. S_LobbyMove가 도착하면 전원 확인으로 처리한다. 추후 서버가 참가자 ID를 알려주면 StageReturnFlow.ApplyResultConfirmation(playerId)에 연결한다.
