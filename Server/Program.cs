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

namespace Server;

class Program
{
    // 모든 접속 세션 저장
    static ConcurrentDictionary<string, IAppSession> sessions = new();

    static async Task Main(string[] args)
    {
        PacketDispatcher.RegisterAll();

        var host = SuperSocketHostBuilder
            .Create<PacketPackageInfo, PacketPipelineFilter>()

            .UseSession<Session>()
            .UseSessionHandler(
                onConnected: async (session) =>
                {
                    sessions[session.SessionID] = session;
                    Console.WriteLine($"Client connected: {session.SessionID}");
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
                PacketDispatcher.Dispatch(session, (PacketId)package.Id, package);
            })

            .ConfigureSuperSocket(options =>
            {
                options.Name = "ChatServer";
                options.Listeners = new List<ListenOptions>
                {
                new ListenOptions
                {
                    Ip = "Any",
                    Port = 4040,
                    BackLog = 10000,
                }
                };
            })

            .Build();

        await host.RunAsync();
    }
}
