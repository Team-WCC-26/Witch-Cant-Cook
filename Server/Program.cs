using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using SuperSocket.Server.Abstractions.Session;
using SuperSocket.Server.Host;
using SuperSocket.Server.Abstractions;
using Protocol;
using Newtonsoft.Json;

namespace Server;

class Program
{
    // 모든 접속 세션 저장
    static ConcurrentDictionary<string, IAppSession> sessions = new();

    static async Task Main(string[] args)
    {
        PacketDispatcher.RegisterAll();

        using (HttpClient client = new())
        {
            string url = "https://script.google.com/macros/s/AKfycbzTL3tVHIradyC9ZqIlz5agPNYQIhtxsQUYsWCxlvweYUPtpdaZEPfMzL8budqDN-t4/exec";
            string export = "?exportSheet=";
            string ingredient = "Ingredient";
            string ingredientCombination = "IngredientCombination";
            string dish = "Recipe";
            var DB = ServerContext.Instance.DataBase;

            string json = await client.GetStringAsync(url + export + ingredient);
            DB.Ingredients = JsonConvert.DeserializeObject<List<IngredientData>>(json).ToDictionary(x => x.Id);

            json = await client.GetStringAsync(url + export + ingredientCombination);
            DB.IngredientsCombination = JsonConvert.DeserializeObject<List<IngredientCombinationData>>(json).ToDictionary(x => x.Id);

            json = await client.GetStringAsync(url + export + dish);
            DB.DishData = JsonConvert.DeserializeObject<List<DishData>>(json).ToDictionary(x => x.Id);
        };

        var host = SuperSocketHostBuilder
            .Create<PacketPackageInfo, PacketPipelineFilter>()

            .UseSession<Session>()
            .UseSessionHandler(
                onConnected: (session) =>
                {
                    sessions[session.SessionID] = session;
                    Console.WriteLine($"Client connected: {session.SessionID}");
                    return ValueTask.CompletedTask;
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
            //.UseUdp()
            .Build();

        var thread = new Thread(() =>
        {
            ServerContext.Instance.RoomManager.Loop();
        });

        thread.IsBackground = true;
        thread.Start();

        await host.RunAsync();
    }
}
