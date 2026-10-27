using Protocol;

namespace Server;

public class GameLoopHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_StageSet)]
    public static void SetStage(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<StageSetPacket>(package.Body);

        session.Player.PushGameplayJob(session, room =>
        {
            // 유효하지 않은 스테이지 번호 요청은 무시한다 (룸 상태 오염 방지)
            if (!room.SetStage(packet.StageNum)) return;

            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_StageStart)]
    public static void StartStage(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<StageStartPacket>(package.Body);

        session.Player.PushGameplayJob(session, room =>
        {
            // 유효하지 않은 스테이지 번호로는 시작하지 않는다 (기존 스테이지 상태 유지)
            if (!room.SetStage(packet.StageNum)) return;

            room.Start();

            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_StageStop)]
    public static void StopStage(Session session, PacketPackageInfo package)
    {
        session.Player.PushGameplayJob(session, room =>
        {
            room.EndStage();
        });
    }

    [PacketHandler(PacketId.C_StageResultConfirm)]
    public static void ConfirmStageResult(Session session, PacketPackageInfo package)
    {
        session.Player.PushGameplayJob(session, room =>
        {
            room.ConfirmStageResult(session.Player);
        });
    }
}
