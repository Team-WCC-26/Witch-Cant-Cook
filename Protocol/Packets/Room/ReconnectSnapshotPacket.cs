using MemoryPack;
using System.Numerics;

namespace Protocol;

/// <summary>
/// 서버 → 클라이언트 재접속 전체 상태 스냅샷 (청크 분할 전송)
/// <para/> Payload는 SnapshotData를 MemoryPack 직렬화한 바이트의 청크
/// (클라 수신 버퍼 64KiB 제한 대비 ~48KB 단위로 분할한다)
/// </summary>
[MemoryPackable]
[PacketId(PacketId.S_ReconnectSnapshot)]
public partial class ReconnectSnapshotPacket
{
    public long SnapshotId { get; set; }
    public int ChunkIndex { get; set; }
    public int ChunkCount { get; set; }
    public bool IsFinal { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// 스냅샷 Entity 구분 (EntitySnapshot.Kind)
/// </summary>
public enum SnapshotEntityKind : byte
{
    Tool = 0,
    Ingredient = 1,
    Player = 2,
    FixedObject = 3, // 룸 고정 오브젝트 (도구가 아니고 클라에서 위치를 알고 있는 것)
}

/// <summary>
/// 재접속 시 서버 현재 상태 전체 스냅샷 DTO (ReconnectSnapshotPacket.Payload의 직렬화 본체)
/// <para/> 서버가 추적하지 않는 값(도구/재료 Transform 등)은 포함하지 않는다
/// </summary>
[MemoryPackable]
public partial class SnapshotData
{
    public string RoomId { get; set; }
    public int Stage { get; set; }
    public bool StageRunning { get; set; }
    public long StageRemainingMs { get; set; } // 스테이지 타이머 남은 시간 (정지 상태면 0)
    public int Money { get; set; }
    public int PendingMoney { get; set; }
    public List<PlayerSnapshot> Players { get; set; } = new();
    public List<EntitySnapshot> Entities { get; set; } = new();
    public List<DoorSnapshot> Doors { get; set; } = new();
    public List<DishSnapshot> DishOrders { get; set; } = new();
    public List<SpawnerSnapshot> Spawners { get; set; } = new();    // 벨트별 스포너 식별자 + 다음 생성 남은 시간
    public long NextOrderRemainingMs { get; set; }                  // 다음 주문 생성 딜레이 남은 시간
    public StageResultSnapshot? StageResult { get; set; }           // 정산 결과 확인 대기 중일 때만 설정 (null이 아니면 클라는 정산 UI를 띄운다)
    public List<ReplayEventSnapshot> ReplayEvents { get; set; } = new(); // 재접속 유예 창 동안의 위치변화 이벤트 재생 목록 (시간 순서)
}

/// <summary>
/// 재생 이벤트 구분 (ReplayEventSnapshot.Kind)
/// <para/> 재접속 유예 창 동안 브로드캐스트된 위치변화 이벤트의 원본 패킷 종류
/// </summary>
public enum ReplayEventKind : byte
{
    None = 0,
    Throw = 1,        // S_EntityThrow (던지기)
    PanEject = 2,     // S_PanEject (프라이팬 배출)
    PotEject = 3,     // S_PotEject (냄비 배출)
    ToolSpawn = 4,    // S_ToolSpawn (도구 생성)
    ToolRegister = 5, // S_ToolRegister (도구 등록)
    ConveySpawn = 6,  // S_IngredientConveySpawn (벨트 재료 스폰)
}

/// <summary>
/// 재접속 유예 창 동안의 위치변화 이벤트 재생 항목 (SnapshotData.ReplayEvents)
/// <para/> 이벤트 원본 패킷 데이터를 Kind에 따라 해석한다:
/// <para/> - Throw: EntityId/Position/Velocity 유효 (S_EntityThrow 준용)
/// <para/> - PanEject: EntityId=프라이팬, RelatedEntityId=배출 재료, Position/Seed 유효 (S_PanEject 준용)
/// <para/> - PotEject: EntityId=냄비, EntityIds=배출 엔티티, PlayerIds=내부 플레이어, Seed/DelayMs 유효 (S_PotEject 준용)
/// <para/> - ToolSpawn/ToolRegister: EntityId/ToolId/Position/Rotation 유효 (S_ToolSpawn/S_ToolRegister 준용)
/// <para/> - ConveySpawn: EntityId/IngredientId/ConveyId 유효, 위치는 ConveyId(벨트 스폰 지점)로 산출
/// </summary>
[MemoryPackable]
public partial class ReplayEventSnapshot
{
    public ReplayEventKind Kind { get; set; }
    public long RecordedAtMs { get; set; }                // 기록 시각 (모노토닉, Environment.TickCount64)
    public long EntityId { get; set; }
    public long RelatedEntityId { get; set; } = -1;       // 보조 엔티티 (PanEject: 배출 재료 등, 없으면 -1)
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public Quaternion Rotation { get; set; }
    public int Seed { get; set; }                         // 배출 시뮬레이션 시드 (PanEject/PotEject만 유효)
    public long DelayMs { get; set; }                     // 배출 딜레이 (PotEject만 유효)
    public int ToolId { get; set; }                       // ToolSpawn/ToolRegister만 유효
    public int IngredientId { get; set; }                 // ConveySpawn만 유효
    public int ConveyId { get; set; }                     // ConveySpawn만 유효
    public List<long> EntityIds { get; set; } = new();    // PotEject 배출 엔티티 목록
    public List<string> PlayerIds { get; set; } = new();  // PotEject 내부에 있던 플레이어 목록
}

/// <summary>
/// 스냅샷 플레이어 상태 (기존 Player 동기화 필드 준용)
/// </summary>
[MemoryPackable]
public partial class PlayerSnapshot
{
    public string StablePlayerId { get; set; }
    public long EntityId { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public PlayerCombinedState State { get; set; }
    public int Hp { get; set; }
    public float Ping { get; set; }
    public long ParentEntityId { get; set; } = -1;   // -1 = 없음 (컨테이너 도구 안에 있을 때)
    public long HoldingEntityId { get; set; } = -1;  // -1 = 쥔 것 없음
}

/// <summary>
/// 스냅샷 Entity 상태 (도구/재료 공통, 플레이어는 PlayerSnapshot으로 전달)
/// </summary>
[MemoryPackable]
public partial class EntitySnapshot
{
    public long EntityId { get; set; }
    public SnapshotEntityKind Kind { get; set; }
    public int ToolId { get; set; }             // Tool만 유효
    public int IngredientId { get; set; }       // Ingredient만 유효
    public IngredientState ProcessState { get; set; } // Ingredient만 유효
    public int IngredientHp { get; set; }       // Ingredient만 유효
    public long ParentEntityId { get; set; } = -1;    // -1 = 없음
    public string? ParentPlayerId { get; set; }       // 플레이어에게 잡혀 있을 때 설정
    public List<long> ChildEntityIds { get; set; } = new(); // 컨테이너 자식 EntityId 목록
    public long CookRemainingMs { get; set; }   // 조리 중인 도구의 조리 남은 시간 (아니면 0)
    public IngredientEffectSnapshot? Effect { get; set; } // 특수재료 현재 효과 상태
    public JumpPadSnapshot? JumpPad { get; set; }         // 점프대 현재 상태
    public LeaveRoomTriggerSnapshot? LeaveRoomTrigger { get; set; } // 퇴장 트리거 현재 상태
}

/// <summary>
/// 스냅샷 점프대 상태 (장전 상태/탑승자/발사 회차/타이머 남은 시간)
/// </summary>
[MemoryPackable]
public partial class JumpPadSnapshot
{
    public JumpPadChargeState State { get; set; }
    public int RiderCount { get; set; }
    public int FireRound { get; set; }          // 발사 회차 (JumpPadEventPacket.FireRound와 동일 기준)
    public long ChargeRemainingMs { get; set; } // 장전 타이머 남은 시간 (장전 중이 아니면 0)
    public long ReturnRemainingMs { get; set; } // 복귀 타이머 남은 시간 (복귀 중이 아니면 0)
}

/// <summary>
/// 스냅샷 퇴장 트리거 상태 (공유 상태와 Idle 복귀 타이머 남은 시간)
/// </summary>
[MemoryPackable]
public partial class LeaveRoomTriggerSnapshot
{
    public LeaveRoomTriggerState State { get; set; }
    public long RevertRemainingMs { get; set; } // Idle 복귀 타이머 남은 시간 (Active가 아니면 0)
}

/// <summary>
/// 특수재료의 현재 진행 중인 효과 상태 (IngredientActivationPacket의 상태 체계 준용)
/// </summary>
[MemoryPackable]
public partial class IngredientEffectSnapshot
{
    public IngredientActivationState State { get; set; }
    public long RemainingMs { get; set; }
    public string? TargetPlayerId { get; set; }
}

/// <summary>
/// 스냅샷 문 상태
/// </summary>
[MemoryPackable]
public partial class DoorSnapshot
{
    public DoorId DoorId { get; set; }
    public bool IsOpen { get; set; }
    public long OpenRemainingMs { get; set; }   // 열림 예정까지 남은 시간 (열림 대기 중이 아니면 0)
}

/// <summary>
/// 스냅샷 스포너 상태 (벨트 식별자와 다음 생성 남은 시간)
/// </summary>
[MemoryPackable]
public partial class SpawnerSnapshot
{
    public int ConveyId { get; set; }
    public long RemainingMs { get; set; }
}

/// <summary>
/// 스냅샷 주문 상태 (진행 중인 주문과 남은 시한)
/// </summary>
[MemoryPackable]
public partial class DishSnapshot
{
    public int RecipeId { get; set; }
    public long RemainingMs { get; set; }
}

/// <summary>
/// 스냅샷 정산 결과 상태 (정산 결과 확인 대기 중일 때 SnapshotData.StageResult로 설정)
/// <para/> 확인 대기가 아닌 스테이지 종료 상태(로비 복귀 완료)는 StageRunning=false + 로비 문 열림(Doors)으로 판단한다
/// </summary>
[MemoryPackable]
public partial class StageResultSnapshot
{
    public int PendingMoney { get; set; } // 획득 예정량 (정산 확정분)
    public int Money { get; set; }        // 최종 보유량 (정산 후)
    public bool IsSuccess { get; set; }   // 스테이지 성공 여부
}
