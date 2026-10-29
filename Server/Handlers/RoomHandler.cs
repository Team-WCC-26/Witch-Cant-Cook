using Protocol;

namespace Server;

public class RoomHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_CreateRoom)]
    public static void CreateRoom(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<CreateRoomPacket>(package.Body);

        Player player = session.Player;

        // 방 생성은 참가 확정 샤드 작업과 묶는다: 참가가 펜싱으로 폐기되면(종료 경합)
        // 생성된 방을 회수해 유령 빈 방이 남지 않게 한다.
        // 회수는 가드 헬퍼로: 파괴되지 않았고 비어 있을 때만 제거하며, 대기 중/직후 참가가
        // 성공한 방은 파괴를 보류하고 이후 Leave → 인원 0 → RemoveRoom 경로로 회수된다
        Room newRoom = ServerContext.Instance.RoomManager.CreateRoom(packet.RoomName, packet.RoomPassword);

        PushJoin(session, player, newRoom, onDropped: () => ServerContext.Instance.RoomManager.RemoveRoomIfEmpty(newRoom.Id));
    }

    [PacketHandler(PacketId.C_GetRoom)]
    public static void GetRoom(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<GetRoomPacket>(package.Body);
        var rooms = ServerContext.Instance.RoomManager.GetEnableRooms();

        packet.RoomDatas.Clear();

        foreach (var room in rooms)
        {
            packet.RoomDatas.Add(new RoomData()
            {
                Id = room.Id,
                Name = room.Name,
                BIsPrivate = !string.IsNullOrEmpty(room.Password)
            });
        }

        session.Player.Send(PacketSerializer.Serialize(packet, true));
    }

    [PacketHandler(PacketId.C_JoinRoom)]
    public static void JoinRoom(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<JoinRoomPacket>(package.Body);
        var room = ServerContext.Instance.RoomManager.GetRoom(packet.RoomId);

        if (room == null)
        {
            JoinFail(session);
            return;
        }

        if (string.IsNullOrEmpty(room.Password) || room.Password == packet?.RoomPassword)
        {
            JoinRoom(session, room);
        }
    }

    [PacketHandler(PacketId.C_LeaveRoom)]
    public static void LeaveRoom(Session session, PacketPackageInfo package)
    {
        // 룸 퇴장도 펜싱 경로로 통일한다 (Player.LeaveRoom에서 수신 세션 기준 재검증)
        session.Player.LeaveRoom(session);
    }

    public static void JoinRoom(Session session, Room room)
    {
        if (room == null)
        {
            JoinFail(session);
            return;
        }

        PushJoin(session, session.Player, room);
    }

    // 룸 참가 요청을 펜싱 샤드 작업으로 푸시한다
    // onDropped: 펜싱 실패로 작업이 폐기될 때의 롤백 (생성 직후 방 회수 등)
    private static void PushJoin(Session session, Player player, Room room, Action? onDropped = null)
    {
        int generation = session.BindingGeneration;

        player.PushGameplayJob(session, room, targetRoom =>
        {
            // 멱등 재참가 검사를 정원/파괴 검사보다 먼저 수행한다: 만원(2인) 방의 같은 룸 재요청도
            // 성공 응답을 받는다 (rebind 직후 같은 방 재참가 / JoinRoom 응답 유실 후 재시도 지원).
            // 이 분기는 Enter를 호출하지 않으므로 중복 등록이 발생하지 않는다
            if (ReferenceEquals(player.Room, targetRoom))
            {
                SendJoinSuccess(player, targetRoom);

                return;
            }

            // 파괴된/만원 방 참가 거부 (참가 작업 대기 중 방이 회수된 경우 포함)
            if (!targetRoom.IsEnable())
            {
                JoinFail(session, false);

                if (onDropped != null)
                {
                    onDropped();
                }

                return;
            }

            // 다른 룸에 속한 플레이어의 참가 요청은 거부한다 (이중 멤버십 방지).
            // 생성 직후 방에 대한 요청이 거부되면(이미 다른 룸 소속) 생성된 방을 회수한다
            if (player.Room != null)
            {
                JoinFail(session, false);

                if (onDropped != null)
                {
                    onDropped();
                }

                return;
            }

            PlayerEnterPacket playerEnterPacket = new()
            {
                NewPlayerID = player.PlayerId
            };

            var packet = PacketSerializer.Serialize(playerEnterPacket, true);

            foreach (var roomPlayer in targetRoom.Players)
            {
                roomPlayer.Send(packet);
            }

            targetRoom.Enter(player);

            // 참가 확정 직후 재검증: 참가 도중 세션이 끊겼으면 즉시 퇴장으로 롤백한다.
            // 레코드가 있으면 record lock으로 종료 처리와 상호 배제한다:
            // 종료 처리가 먼저면 여기서 롤백, 참가가 먼저면 종료 측이 룸을 관측해 정리 job을 큐잉한다
            // (어느 쪽이든 플레이어가 룸에 영구 잔류하지 않는다.
            //  S_PlayerEnter를 받은 클라가 있으면 Leave가 S_PlayerLeave도 브로드캐스트한다)
            if (!IsJoinBindingValid(session, player, generation))
            {
                targetRoom.Leave(player);

                return;
            }

            SendJoinSuccess(player, targetRoom);
        },
        onDropped);
    }

    // 참가 확정 직후 바인딩 유효성 검사 (샤드 작업 내 재검증)
    private static bool IsJoinBindingValid(Session session, Player player, int generation)
    {
        SessionRecord? record = session.BoundRecord;

        if (record == null)
        {
            return player.ConnectionState == PlayerConnectionState.Live
                && ReferenceEquals(player.Session, session)
                && session.BindingGeneration == generation;
        }

        lock (record.Lock)
        {
            return record.State == SessionRecordState.Active
                && player.ConnectionState == PlayerConnectionState.Live
                && ReferenceEquals(player.Session, session)
                && session.BindingGeneration == generation;
        }
    }

    // 참가 성공 응답 전송 (최초 참가와 멱등 재요청이 동일한 응답을 받는다)
    // OtherPlayerId는 요청 플레이어 자신을 제외한 상대 플레이어 ID다 (재참가자가 Players[0]일 때도
    // 자기 자신 ID를 반환하지 않는다, 룸에 상대가 없으면 빈 값)
    private static void SendJoinSuccess(Player player, Room targetRoom)
    {
        JoinRoomPacket joinRoomPacket = new()
        {
            PlayerId = player.PlayerId,
            RoomId = targetRoom.Id,
            PlayerCnt = targetRoom.PlayerCnt,
            OtherPlayerId = targetRoom.Players.FirstOrDefault(other => !ReferenceEquals(other, player))?.PlayerId ?? ""
        };

        player.Send(PacketSerializer.Serialize(joinRoomPacket, true));

        RoomMoneyPacket roomMoneyPacket = new()
        {
            Money = targetRoom.Money
        };

        player.Send(PacketSerializer.Serialize(roomMoneyPacket, true));

        targetRoom.SendLeaveRoomTriggerState(player);
    }

    private static void JoinFail(Session session, bool isRoomNull = true)
    {
        JoinRoomPacket packet = new()
        {
            RoomId = isRoomNull ? "N" : "F"
        };

        session.Player.Send(PacketSerializer.Serialize(packet, true));
    }
}
