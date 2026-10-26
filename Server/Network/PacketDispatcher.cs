using System.Collections.Concurrent;
using System.Reflection;
using Protocol;
using SuperSocket.Server.Abstractions.Session;

namespace Server;

public static class PacketDispatcher
{
    private static readonly ConcurrentDictionary<PacketId, Action<IAppSession, PacketPackageInfo>> _handlers = new();

    public static void RegisterAll()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();

        foreach (var type in types)
        {
            if (!typeof(PacketHandlerBase).IsAssignableFrom(type)) continue;

            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<PacketHandlerAttribute>();

                if (attr == null) continue;
                 
                var parameters = method.GetParameters();

                if (parameters.Length != 2)
                {
                    throw new Exception($"Invalid handler signature: {method.Name}");
                }

                var packetType = parameters[1].ParameterType;

                Action<IAppSession, PacketPackageInfo> action = (session, packet) =>
                {
                    method.Invoke(null, [session, packet]);
                };

                _handlers[attr.PacketId] = action;
            }
        }
    }

    public static void Dispatch(IAppSession session, PacketId id, PacketPackageInfo package)
    {
        if (((ushort)id & 1) > 0) // 클라 패킷이 아닐 경우
        {
            Console.WriteLine($"Wrong Packet Id: {id}");
            return;
        }

        // 모든 수신 경로(직접/지연 시뮬 포함)의 단일 진입점에서 수신 시각을 갱신한다 (하트비트 타임아웃 판정용)
        if (session is Session gameSession)
        {
            gameSession.LastReceivedAtMs = Environment.TickCount64;

            // 세션 협상/연결 상태 게이팅: 허용되지 않은 패킷은 폐기한다
            if (!gameSession.AllowDispatch(id)) return;
        }

        if (_handlers.TryGetValue(id, out var handler))
        {
            handler(session, package);
        }
        else
        {
            Console.WriteLine($"Unhandled packet: {id}");
        }
    }
}
