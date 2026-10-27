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
            room.SetStage(packet.StageNum);

            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_StageStart)]
    public static void StartStage(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<StageStartPacket>(package.Body);

        session.Player.PushGameplayJob(session, room =>
        {
            room.SetStage(packet.StageNum);
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
