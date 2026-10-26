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
