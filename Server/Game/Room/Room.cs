using Protocol;

namespace Server;

public class Room
{
    public string Id { get; }
    public string Name { get; }
    public string Password { get; }

    public IReadOnlyList<Player> Players => _players;
    private readonly List<Player> _players = new();
    public readonly int MaxPlayerCount = 2;

    public IReadOnlyDictionary<long, Entity> Entities => _entities;
    private readonly Dictionary<long, Entity> _entities = new();

    private readonly HashSet<Entity> _dirtyEntities = new();

    public int PlayerCnt => _playerCnt;
    private int _playerCnt = 0;
    private int _tick = 0;

    // 룸 공용 재화 (요리 제출 성공 시 지급, 주방 업그레이드에 사용)
    public int Money => _money;
    private int _money = 0;

    // 스테이지 종료 시 보유량에 정산되는 임시 보유량 (획득 예정량)
    public int PendingMoney => _pendingMoney;
    private int _pendingMoney = 0;

    // 현재 스테이지 (재접속 스냅샷 동기화용)
    public int Stage => _stage;
    private int _stage = 0;

    private long _nextEntityId = 0;

    // 룸 소멸 여부 (RoomManager.RemoveRoom에서만 설정되며 되돌리지 않음)
    // 주의: Stop()은 스테이지 정리일 뿐 소멸이 아니므로 이 플래그를 건드리지 않는다
    public bool IsDestroyed => _destroyed;
    private volatile bool _destroyed = false;

    // 작업 실행 샤드는 JobQueue가 단일 출처로 관리한다
    private readonly JobQueue _jobQueue;

    private readonly Dictionary<DoorId, Door> _doors = new();

    public IReadOnlyDictionary<DoorId, Door> Doors => _doors;

    public TimerManager TimerManager => _timerManager;
    private TimerManager _timerManager = new();

    // === 부하 측정 (소유 샤드 스레드 단일 작성자, 리밸런서는 근사 읽기) ===
    public long TickCostEmaUs => Volatile.Read(ref _tickCostEmaUs);
    private long _tickCostEmaUs = 0; // Room.Tick 비용 EMA (μs)

    public long JobCostEmaUs => Volatile.Read(ref _jobCostEmaUs);
    private long _jobCostEmaUs = 0; // 작업 1개당 비용 EMA (μs)

    public long JobCount => Volatile.Read(ref _jobCount);
    private long _jobCount = 0; // 누적 실행 작업 수

    private const long StagePlayTimeMs = 4 * 60 * 1000;

    private TimerHandle? _stageTimer;

    // 스테이지 종료 후 정산 유예 타이머 (룸 핑 최대값만큼 제출을 더 받는다)
    private TimerHandle? _settleTimer;

    // 정산 결과 전송 후 룸의 모든 플레이어 확인 대기 중 여부
    private bool _awaitingResultConfirm = false;

    // 정산 결과를 확인한 플레이어 (전원 확인 시 로비로 복귀)
    private readonly HashSet<string> _stageResultConfirms = new();

    // 마지막 정산 결과 (확인 대기 중 재접속한 플레이어의 스냅샷 동기화용)
    private StageResultSnapshot? _lastStageResult;

    private IngredientSpawner _ingredientSpawner;
    private DishManager _dishManager;

    // 룸 고정 퇴장 트리거 (룸 수명 엔티티: 스테이지별 생성/정리 대상에서 제외되고 Init에서 1회 등록한다)
    private LeaveRoomTrigger? _leaveRoomTrigger;

    public Room(string id, string name, string password)
    {
        Id = id;
        Name = name;
        Password = password;

        _jobQueue = new(this);
        _ingredientSpawner = new(this, _timerManager, 2000);
        _dishManager = new(this, _timerManager);
    }

    public void Init()
    {
        _players.Clear();
        _playerCnt = 0;
        _tick = 0;
        _money = 0;
        _pendingMoney = 0;
        _nextEntityId = 0;

        // 퇴장 트리거를 룸 수명 엔티티로 등록한다 (엔티티 Id 초기화 직후 생성)
        LeaveRoomTrigger trigger = new();
        RegisterEntity(GenerateEntityId(), trigger);
        _leaveRoomTrigger = trigger;

        _doors.Clear();

        _doors[DoorId.Lobby] = new(DoorId.Lobby, 2, 3, _timerManager, HandleDoorOpened);
        _doors[DoorId.Kitchen] = new(DoorId.Kitchen, 2, 3, _timerManager, HandleDoorOpened);

        if (!SetStage(1))
        {
            throw new InvalidOperationException("Stage 1 데이터가 존재하지 않습니다.");
        }
    }

    public void Start()
    {
        ResetLeaveRoomTrigger();

        if (_stageTimer != null)
        {
            _timerManager.Cancel(_stageTimer.Value);
        }

        // 이전 스테이지의 정산 유예/확인 대기가 남아있으면 정리한다 (재시작 시 이중 정산 방지)
        if (_settleTimer != null)
        {
            _timerManager.Cancel(_settleTimer.Value);
            _settleTimer = null;
        }

        _awaitingResultConfirm = false;
        _stageResultConfirms.Clear();
        _lastStageResult = null;

        _stageTimer = _timerManager.Schedule(StagePlayTimeMs, this, static room => room.EndStage());

        _dishManager.Start();
        _ingredientSpawner.Start();
    }

    public void Stop()
    {
        _dishManager.Stop();
        _ingredientSpawner.Stop();
        ResetLeaveRoomTrigger();
        _timerManager.Clear();
        _stageTimer = null;
        _settleTimer = null;
        _awaitingResultConfirm = false;
        _stageResultConfirms.Clear();
        _lastStageResult = null;
    }

    public void Tick(long deltaTime)
    {
        _timerManager.Tick(deltaTime);

        WorldStatePacket packet = new()
        {
            Tick = _tick
        };

        foreach (var player in _players)
        {
            var mask = player.ConsumeDirtyMask();
            player.WriteSnapShot(packet, mask);
        }

        foreach (var entity in _dirtyEntities)
        {
            var mask = entity.ConsumeDirtyMask();
            entity.WriteSnapShot(packet, mask);
        }

        _dirtyEntities.Clear();

        BroadCast(PacketSerializer.Serialize(packet, true));

        FlushSend();

        _tick++;
    }

    // 스테이지 진행 중 여부 (정산 유예/확인 대기는 진행 중이 아닌 것으로 본다)
    public bool IsStageRunning => _stageTimer != null;

    public bool IsEnable()
    {
        // 파괴된 방은 참가 대상에서 제외한다 (회수 경합에서 대기 중 참가 작업 방어)
        // 스테이지 진행/정산 유예/확인 대기 중인 방은 신규 참가를 받지 않는다 (게임 시작 전에만 참가 가능)
        return !_destroyed
            && _stageTimer == null
            && _settleTimer == null
            && !_awaitingResultConfirm
            && _playerCnt >= 0
            && _playerCnt < MaxPlayerCount;
    }

    public void PushJob(Action job) => _jobQueue.Push(job);

    public void InitShard(Shard shard) => _jobQueue.InitShard(shard);

    // 마이그레이션 시작 (소스 샤드 스레드의 마이그레이션 작업 M 내부에서 호출)
    public void BeginMigration(Shard target) => _jobQueue.BeginMigration(target);

    public Shard GetShard()
    {
        return _jobQueue.CurrentShard!;
    }

    // 마지막 재배치 시각 (리밸런서가 기록, 룸 단위 재배치 쿨다운용)
    public long LastMovedAtMs { get; private set; }

    public void MarkMoved() => LastMovedAtMs = TimeUtil.NowMs();

    // 룸 소멸 표시 (RoomManager.RemoveRoom에서만 호출 - 이후 실행되는 샤드 등록 job이 등록하지 않도록)
    public void MarkDestroyed() => _destroyed = true;

    // 샤드 루프가 Room.Tick 하나를 실행한 뒤 호출 (소유 샤드 스레드 전용)
    public void RecordTickCost(long costUs)
    {
        _tickCostEmaUs += (long)((costUs - _tickCostEmaUs) * ShardBalanceConfig.EmaAlpha);
    }

    // JobQueue가 작업 하나를 실행한 뒤 호출 (소유 샤드 스레드 전용)
    public void RecordJobExecuted(long costUs)
    {
        _jobCostEmaUs += (long)((costUs - _jobCostEmaUs) * ShardBalanceConfig.EmaAlpha);
        _jobCount++;
    }

    public void MakeDirty(Entity entity)
    {
        // 플레이어는 Tick에서 _players 순회로 직렬화하므로 엔티티 더티 집합에서 제외한다
        if (entity is Player) return;

        _dirtyEntities.Add(entity);
    }

    // 유효한 스테이지만 설정한다 (잘못된 번호는 거부하고 false 반환 — _stage 오염 방지)
    public bool SetStage(int stage)
    {
        var db = ServerContext.Instance.DataBase;

        if (!db.IngredientGroups.ContainsKey(stage)) return false;
        if (!db.RecipeGroups.ContainsKey(stage)) return false;
        if (!db.Stages.ContainsKey(stage)) return false;

        _stage = stage;

        _ingredientSpawner.SetStage(stage);
        _dishManager.SetStage(stage);

        return true;
    }

    public Ingredient GenerateIngredient(int id, out long entityId)
    {
        Ingredient ingredient = new();
        entityId = GenerateEntityId();

        RegisterEntity(entityId, ingredient);
        ingredient.InitIngredientId(id);

        return ingredient;
    }

    public Tool? GenerateTool(int id, out long entityId)
    {
        Tool? tool;

        switch (id)
        {
            case 10:
                tool = new Knife();
                break;

            case 20:
                tool = new Pan();
                break;

            case 30:
                tool = new Stove();
                break;

            case 40:
                tool = new Pot();
                break;

            case 50:
                tool = new Dish();
                break;

            case 60:
                tool = new CounterTop();
                break;

            case 70:
                tool = new JumpPad();
                break;

            case 80:
                tool = new Oven();
                //_cookManager.RegisterCookingTool(id, tool as CookingTool);
                break;

            case 90:
                tool = new TrashCan();
                break;

            case 100:
                tool = new CookingSubmit();
                break;

            default:
                tool = null;
                break;
        }

        if (tool == null)
        {
            entityId = 0;
            return null;
        }

        entityId = GenerateEntityId();
        tool.InitToolId(id);

        RegisterEntity(entityId, tool);

        return tool;
    }

    //public void CombineEntity(long resultId, long removeId, Entity entity)
    //{
    //    _entities.Remove(removeId);

    //    _entities[resultId] = entity;
    //}

    //public void UpdateEntity(long entityId, Entity entity)
    //{
    //    _entities[entityId] = entity;
    //}

    private long GenerateEntityId()
    {
        long newId = Interlocked.Increment(ref _nextEntityId);

        return newId;
    }

    /// <summary>
    /// Room 접속
    /// <para/> 사용시 PushJob안에 넣어줘야 함
    /// </summary>
    public void Enter(Player player)
    {
        // 파괴된 방 참가 금지 (참가 작업 대기 중 방이 회수된 경우)
        if (_destroyed) return;

        // 중복 참가 방지 (동일 Player가 두 번 등록되지 않게 한다)
        if (_players.Contains(player)) return;

        // player.Room을 멤버십 등록 전에 설정한다: 참가 작업 실행 중 종료 이벤트가 룸을 읽는 경합에서
        // 멤버십 누락 판정(정리 누락/레코드 오만료)이 나오지 않게 한다
        player.AttachRoom(this);
        _players.Add(player);
        _playerCnt++;
    }

    /// <summary>
    /// Room 접속 해제
    /// <para/> 사용시 PushJob안에 넣어줘야 함
    /// </summary>
    public void Leave(Player player)
    {
        // 등록되지 않은 플레이어의 퇴장은 무시한다 (playerCnt 언더플로 가드)
        if (!_players.Remove(player)) return;

        player.AttachRoom(null!);

        if (player.Parent is CookingTool cookingTool)
        {
            cookingTool.RemovePlayer(player);
        }

        if (--_playerCnt <= 0)
        {
            ServerContext.Instance.RoomManager.RemoveRoom(Id);
        }

        PlayerLeavePacket packet = new()
        {
            PlayerID = player.PlayerId
        };

        BroadCast(PacketSerializer.Serialize(packet, true));

        // 정산 확인 대기 중 인원이 줄면 남은 인원 기준으로 로비 복귀를 재평가한다
        CheckAllConfirmed();
    }

    /// <summary>
    /// 유예 만료/명시적 종료된 플레이어의 최종 정리
    /// <para/> 사용시 PushJob안에 넣어줘야 함. 호출부에서 Detached 상태를 재검사해 이미 재접속됐으면 실행하지 않는다
    /// </summary>
    public void RemovePlayerFinal(Player player)
    {
        // 이미 나갔거나 재접속으로 복귀한 경우는 정리하지 않는다 (실행 시점 재검사)
        if (!_players.Contains(player)) return;

        // 쥐고 있던 Entity를 떨어뜨린다 (부모 해제 시 Player.HoldingEntity도 함께 정리된다)
        if (player.HoldingEntity != null)
        {
            player.HoldingEntity.Parent = null;
        }

        Leave(player);
    }

    /// <summary>
    /// 재접속 스냅샷 생성: 서버가 추적하는 현재 상태를 읽어 만든다 (Dirty Consume 없이 읽기만 한다)
    /// <para/> 반드시 PushJob(샤드 작업) 안에서 호출해 일관 시점에 생성해야 한다
    /// <para/> 서버가 추적하지 않는 값(도구/재료 Transform 등)은 포함하지 않는다
    /// </summary>
    public SnapshotData CreateReconnectSnapshot()
    {
        SnapshotData snapshot = new()
        {
            RoomId = Id,
            Stage = _stage,
            StageRunning = _stageTimer != null,
            StageRemainingMs = (_stageTimer != null) ? _timerManager.RemainingTime(_stageTimer.Value) : 0,
            Money = _money,
            PendingMoney = _pendingMoney
        };

        // 정산 결과 확인 대기 중이면 결과를 싣는다 (유예/확인 대기 중 브로드캐스트는 끊긴 세션에 폐기된다)
        if (_awaitingResultConfirm && _lastStageResult != null)
        {
            snapshot.StageResult = _lastStageResult;
        }

        foreach (var player in _players)
        {
            snapshot.Players.Add(new PlayerSnapshot()
            {
                StablePlayerId = player.PlayerId,
                EntityId = player.EntityId,
                Position = player.Position,
                Rotation = player.Rotation,
                State = player.State,
                Hp = player.Hp,
                Ping = player.Ping,
                ParentEntityId = (player.Parent == null) ? -1 : player.Parent.EntityId,
                HoldingEntityId = (player.HoldingEntity == null) ? -1 : player.HoldingEntity.EntityId
            });
        }

        foreach (var entity in _entities.Values)
        {
            if (entity is Player) continue;
            if (entity.IsDestroyed) continue;

            EntitySnapshot entitySnapshot = new()
            {
                EntityId = entity.EntityId,
                Kind = (entity is LeaveRoomTrigger) ? SnapshotEntityKind.FixedObject
                    : (entity is Ingredient) ? SnapshotEntityKind.Ingredient : SnapshotEntityKind.Tool,
                ParentEntityId = (entity.Parent == null) ? -1 : entity.Parent.EntityId,
                ParentPlayerId = (entity.Parent as Player)?.PlayerId
            };

            switch (entity)
            {
                case Ingredient ingredient:
                    entitySnapshot.IngredientId = ingredient.IngredientId;
                    entitySnapshot.ProcessState = ingredient.ProcessState;
                    entitySnapshot.IngredientHp = ingredient.Hp;

                    if (ingredient.IngredientBehaviour != null && ingredient.IngredientBehaviour.TryGetEffectState(out var effect))
                    {
                        entitySnapshot.Effect = effect;
                    }
                    break;

                case Tool tool:
                    entitySnapshot.ToolId = tool.ToolId;
                    break;
            }

            // 컨테이너 자식 목록
            if (entity is ContainerTool container)
            {
                foreach (var child in container.Items)
                {
                    entitySnapshot.ChildEntityIds.Add(child.EntityId);
                }
            }

            // 조리중 여부 + 남은 시간 / 점프대 상태
            switch (entity)
            {
                case Pot pot:
                    entitySnapshot.CookRemainingMs = pot.GetCookRemainingMs();
                    break;

                case CookingTool cooking:
                    entitySnapshot.CookRemainingMs = cooking.GetCookRemainingMs();
                    break;

                case JumpPad jumpPad:
                    entitySnapshot.JumpPad = jumpPad.GetSnapshot();
                    break;

                case LeaveRoomTrigger leaveRoomTrigger:
                    entitySnapshot.LeaveRoomTrigger = leaveRoomTrigger.GetSnapshot();
                    break;
            }

            snapshot.Entities.Add(entitySnapshot);
        }

        foreach (var door in _doors.Values)
        {
            snapshot.Doors.Add(new DoorSnapshot()
            {
                DoorId = door.DoorId,
                IsOpen = door.IsOpen,
                OpenRemainingMs = door.GetOpenRemainingMs()
            });
        }

        foreach (var (recipeId, remainingMs) in _dishManager.GetActiveOrders())
        {
            snapshot.DishOrders.Add(new DishSnapshot()
            {
                RecipeId = recipeId,
                RemainingMs = remainingMs
            });
        }

        snapshot.NextOrderRemainingMs = _dishManager.GetNextOrderRemainingMs();
        snapshot.Spawners.AddRange(_ingredientSpawner.GetSpawnRemainingList());

        return snapshot;
    }

    public void BroadCastForce(byte[] packet)
    {
        foreach (var player in _players)
        {
            player.Send(packet);
        }
    }

    public void BroadCast(byte[] packet)
    {
        foreach (var player in _players)
        {
            player.AddBatch(packet);
        }
    }
    
    public void Notificate(string message)
    {
        PushJob(() =>
        {
            RoomNotificationPacket packet = new()
            {
                Message = message
            };

            BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    public bool InteractEntity(long entityId, Player player)
    {
        if (!_entities.TryGetValue(entityId, out var entity) || entity.IsDestroyed) return false;
        if (player.HoldingEntity != null && entityId == player.HoldingEntity.EntityId) return false;
        if (entity is not IInteractable interactable) return false;

        return interactable.Interact(player);
    }

    // 퇴장 트리거의 현재 상태를 한 명에게 전송한다 (참가 성공 응답에 사용)
    public void SendLeaveRoomTriggerState(Player player)
    {
        if (_leaveRoomTrigger == null) return;

        player.Send(PacketSerializer.Serialize(_leaveRoomTrigger.CreateStatePacket(null), true));
    }

    public bool InsertEntity(long targetId, long subjectId)
    {
        if (!_entities.TryGetValue(targetId, out var target) || target.IsDestroyed) return false;
        if (!_entities.TryGetValue(subjectId, out var subject) || subject.IsDestroyed) return false;

        if (target is not ContainerTool containerTool) return false;

        return containerTool.Insert(subject);
    }

    public bool EnterToolPlayer(long toolId, Player player)
    {
        if (!_entities.TryGetValue(toolId, out var entity) || entity.IsDestroyed) return false;

        if (entity is Oven || entity is Pot || entity is TrashCan)
        {
            return (entity as ContainerTool).AddPlayer(player);
        }

        return false;
    }

    public bool ExitToolPlayer(long toolId, Player player)
    {
        if (!_entities.TryGetValue(toolId, out var entity) || entity.IsDestroyed) return false;

        if (entity is Oven || entity is Pot || entity is TrashCan)
        {
            return (entity as ContainerTool).RemovePlayer(player);
        }

        return false;
    }

    public bool ForceEjectTool(long toolId)
    {
        if (!_entities.TryGetValue(toolId, out var entity) || entity.IsDestroyed) return false;

        if (entity is Pot pot)
        {
            pot.ForceEject();

            return true;
        }

        return false;
    }

    public bool EnterJumpPad(long padId)
    {
        if (!_entities.TryGetValue(padId, out var entity) || entity.IsDestroyed) return false;
        if (entity is not JumpPad jumpPad) return false;

        return jumpPad.HandlePlayerEnter();
    }

    public bool ExitJumpPad(long padId)
    {
        if (!_entities.TryGetValue(padId, out var entity) || entity.IsDestroyed) return false;
        if (entity is not JumpPad jumpPad) return false;

        return jumpPad.HandlePlayerExit();
    }

    //public bool LeaveStove(long panId)
    //{
    //    if (!_entities.TryGetValue(panId, out var entity) || entity.IsDestroyed) return false;
    //    if (entity is not Pan pan) return false;

    //    return pan.LeaveStove();
    //}

    public void InteractDoor(DoorId doorId, string playerId)
    {
        _doors[doorId].BeginInteract(playerId);
    }

    public void StopInteractDoor(DoorId doorId, string playerId)
    {
        _doors[doorId].EndInteract(playerId);
    }

    public void AddPendingMoney(int amount)
    {
        if (amount <= 0) return;

        _pendingMoney += amount;

        BroadCastPendingMoney();
    }

    public bool TrySpendMoney(int cost)
    {
        if (cost < 0) return false;
        if (_money < cost) return false;

        _money -= cost;

        BroadCastMoney();

        return true;
    }

    private void BroadCastMoney()
    {
        RoomMoneyPacket packet = new()
        {
            Money = _money
        };

        BroadCast(PacketSerializer.Serialize(packet, true));
    }

    private void BroadCastPendingMoney()
    {
        RoomPendingMoneyPacket packet = new()
        {
            Money = _pendingMoney
        };

        BroadCast(PacketSerializer.Serialize(packet, true));
    }

    public bool ServeDish(long dishId)
    {
        if (!Entities.TryGetValue(dishId, out var entity) || entity.IsDestroyed) return false;
        if (entity is not Dish dish || dish.Ingredient == null) return false;

        _dishManager.SubmitDish(dish.Ingredient.IngredientId);

        dish.Destroy();
        dish.Parent = null;

        DishEjectPacket packet = new()
        {
            DishEntityId = dish.EntityId,
            EjectSeed = (int)TimeUtil.NowMs()
        };

        BroadCast(PacketSerializer.Serialize(packet, true));

        return true;
    }

    // 제출구 등 여러 접시를 한 번에 처리하는 도구용 제출 위임
    public void SubmitDish(int ingredientId)
    {
        _dishManager.SubmitDish(ingredientId);
    }

    private void RegisterEntity(long id, Entity entity)
    {
        entity.InitEntityId(id);
        entity.AttachRoom(this);
        _entities[id] = entity;
    }

    public void UnregisterEntity(long id)
    {
        _entities.Remove(id); // GC 효율보면서 필요시 Pool로 반환
    }

    private void FlushSend()
    {
        foreach (var player in _players)
        {
            player.Flush();
        }
    }

    private PlayerMovementPacket GetMovementData(Player player)
    {
        PlayerMovementPacket packet = new()
        {
            PlayerId = player.PlayerId,
            Position = player.Position,
            Rotation = player.Rotation,
            CombinedState = player.State
        };

        return packet;
    }

    private PingResultPacket GetPingData(Player player)
    {
        PingResultPacket packet = new()
        {
            PlayerId = player.PlayerId,
            Ping = player.Ping
        };

        return packet;
    }

    private void HandleDoorOpened(Door door)
    {
        foreach (var d in _doors.Values)
        {
            d.IsOpen = false;
        }

        door.IsOpen = true;

        OpenDoorPacket packet = new()
        {
            DoorId = door.DoorId
        };

        if (door.DoorId == DoorId.Kitchen)
        {
            Start();
        }

        BroadCast(PacketSerializer.Serialize(packet, true));
    }

    /// <summary>
    /// 스테이지 시간 종료 (타임아웃 또는 클라 요청)
    /// <para/> 종료 알림(StageStop)을 전송하고 룸 핑 최대값만큼 제출 유예 후 정산한다 (SettleStage)
    /// </summary>
    public void EndStage()
    {
        // 이미 유예/정산 확인 대기 중이면 무시한다 (타이머 만료와 클라 종료 요청 중복)
        if (_settleTimer != null || _awaitingResultConfirm) return;

        // 새 주문 선택과 재료 스폰만 중지한다 (진행 중 주문의 제출은 유예 기간 동안 계속 받는다)
        _dishManager.StopSelect();
        _ingredientSpawner.Stop();
        ResetLeaveRoomTrigger();
        _timerManager.Clear();
        _stageTimer = null;

        StageStopPacket packet = new();

        BroadCast(PacketSerializer.Serialize(packet, true));

        _settleTimer = _timerManager.Schedule(GetMaxPingMs(), this, static room => room.SettleStage());
    }

    /// <summary>
    /// 정산 결과 확인 (사용시 PushJob안에 넣어줘야 함)
    /// <para/> 룸의 모든 플레이어가 확인하면 로비로 복귀한다
    /// </summary>
    public void ConfirmStageResult(Player player)
    {
        // 정산 결과 전송 전의 확인은 무시한다
        if (!_awaitingResultConfirm) return;
        if (!_players.Contains(player)) return;

        _stageResultConfirms.Add(player.PlayerId);

        CheckAllConfirmed();
    }

    // 유예 시간 종료: 제출 접수를 마감하고 임시 보유량을 보유량에 정산한 뒤 결과를 전송한다
    private void SettleStage()
    {
        _settleTimer = null;

        bool isSuccess = _dishManager.IsStageSuccess();
        int pendingMoney = _pendingMoney;

        // 유예 기간 동안 시작된 조리 타이머 등 잔여 타이머를 정리한다
        Stop();

        _money += pendingMoney;
        _pendingMoney = 0;

        StageResultPacket packet = new()
        {
            PendingMoney = pendingMoney,
            Money = _money,
            IsSuccess = isSuccess
        };

        // 확인 대기 중 재접속하면 브로드캐스트를 받지 못하므로 스냅샷으로 재전달하기 위해 결과를 보관한다
        _lastStageResult = new StageResultSnapshot()
        {
            PendingMoney = packet.PendingMoney,
            Money = packet.Money,
            IsSuccess = packet.IsSuccess
        };

        BroadCast(PacketSerializer.Serialize(packet, true));

        _awaitingResultConfirm = true;
    }

    // 전원이 확인했는지 재평가한다 (확인 추가 또는 퇴장으로 인원 변화 시 호출)
    private void CheckAllConfirmed()
    {
        if (!_awaitingResultConfirm) return;
        if (_players.Count == 0) return;
        if (_stageResultConfirms.Count < _players.Count) return;

        ReturnToLobby();
    }

    // 전원 확인 완료: 로비 문을 열고 이동을 지시한다 (실제 이동 연산은 클라이언트가 수행)
    private void ReturnToLobby()
    {
        _awaitingResultConfirm = false;
        _stageResultConfirms.Clear();
        _lastStageResult = null;

        HandleDoorOpened(_doors[DoorId.Lobby]);

        LobbyMovePacket packet = new();

        BroadCast(PacketSerializer.Serialize(packet, true));

        ClearKitchenEntities();
    }

    // 퇴장 트리거를 대기 상태로 되돌린다 (스테이지 시작/종료/룸 정지에서 호출)
    // 타이머 Clear로 복귀 타이머가 일괄 취소되기 전에 호출해 dangling Active가 남지 않게 한다
    // 상태가 바뀌었을 때만 초기 상태를 브로드캐스트한다
    private void ResetLeaveRoomTrigger()
    {
        if (_leaveRoomTrigger == null) return;
        if (!_leaveRoomTrigger.Reset()) return;

        BroadCast(PacketSerializer.Serialize(_leaveRoomTrigger.CreateStatePacket(null), true));
    }

    // 주방에 남은 엔티티 정리: 재료는 Destroy 동기화로 클라에서도 제거하고,
    // 도구는 다음 스테이지에서 클라가 재등록하므로 동기화 없이 내부 상태 초기화 후 회수한다
    private void ClearKitchenEntities()
    {
        List<Entity> entities = new(_entities.Values);

        foreach (var entity in entities)
        {
            // 퇴장 트리거는 룸 수명 엔티티로 정리 대상에서 제외한다
            if (entity is LeaveRoomTrigger) continue;

            if (entity is Ingredient ingredient)
            {
                if (!ingredient.IsDestroyed)
                {
                    ingredient.Destroy();
                }

                continue;
            }

            if (entity is Tool tool)
            {
                tool.Reset();
            }

            entity.Parent = null;
            UnregisterEntity(entity.EntityId);
        }
    }

    // 룸 플레이어 핑 최대값 (ms) — 스테이지 종료 후 제출 유예 시간
    // 클라 보고 핑이 비정상적으로 커도 유예가 무한정 길어지지 않게 상한으로 클램프한다
    private long GetMaxPingMs()
    {
        const long SettleGraceMaxMs = 1000;

        long maxPing = 0;

        foreach (var player in _players)
        {
            long ping = (long)Math.Ceiling(player.Ping);

            if (ping > maxPing)
            {
                maxPing = ping;
            }
        }

        return Math.Min(maxPing, SettleGraceMaxMs);
    }
}
