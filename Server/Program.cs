using SuperSocket;
using SuperSocket.Server;
using SuperSocket.ProtoBase;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using SuperSocket.Server.Abstractions.Session;
using SuperSocket.Server.Host;
using SuperSocket.Server.Abstractions;
using System.Text;
using Protocol;
using MemoryPack;
using System.Buffers;

namespace Server
{
    class Program
    {
        // 모든 접속 세션 저장
        static ConcurrentDictionary<string, IAppSession> sessions = new();

        static async Task Main(string[] args)
        {
            PacketDispatcher.RegisterAll();

            var host = SuperSocketHostBuilder
                .Create<PacketPackageInfo, PacketPipelineFilter>()

                .UseSessionHandler(
                    onConnected: async (session) =>
                    {
                        sessions[session.SessionID] = session;
                        Console.WriteLine($"Client connected: {session.SessionID}");
                        await session.SendAsync(Encoding.UTF8.GetBytes("      Welcome to chat server\r\n"));
                    },
                    onClosed: (session, reason) =>
                    {
                        sessions.TryRemove(session.SessionID, out _);
                        Console.WriteLine($"Client disconnected: {session.SessionID}");
                        return ValueTask.CompletedTask;
                    })

                .UsePackageDecoder<PacketPackageDecoder>()

                .UsePackageHandler(async (session, package) =>
                {
                    var buffer = package.Body;

                    var chatPacket = MemoryPackSerializer.Deserialize<ChatMessagePacket>(buffer);

                    Console.WriteLine($"[{session.SessionID}] {chatPacket.Message}");

                    // 모든 세션에 메시지 전송
                    foreach (var s in sessions.Values)
                    {
                        await s.SendAsync(Encoding.UTF8.GetBytes($"      [{session.SessionID}] {chatPacket.Message}\r\n"));
                    }
                })

                .ConfigureSuperSocket(options =>
                {
                    options.Name = "ChatServer";
                    options.Listeners = new List<ListenOptions>
                    {
                    new ListenOptions
                    {
                        Ip = "Any",
                        Port = 4040
                    }
                    };
                })

                .Build();

            await host.RunAsync();
        }
    }
}
