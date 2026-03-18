using System;
using System.Net.Sockets;
using System.Text;
using Protocol;
using System.Threading.Tasks;
using MemoryPack;

namespace TestClient;

class Program
{
    public static bool IsJoinedRoom = false;

    static async Task Main(string[] args)
    {
        var client = new TcpClient();

        await client.ConnectAsync("127.0.0.1", 4040);

        Console.WriteLine("Connected to server");

        var stream = client.GetStream();

        PacketReceiver packetReceiver = new();

        _ = Task.Run(async () =>
        {
            try
            {
                await packetReceiver.StartAsync(stream);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Disconnected] {e.Message}");
                Environment.Exit(0);
            }
        });

        // 메시지 입력
        while (true)
        {
            var input = Console.ReadLine();

            Console.SetCursorPosition(0, Console.CursorTop - 1);
            Console.Write("\r" + new string(' ', Console.BufferWidth) + "\r");
            Console.SetCursorPosition(0, Console.CursorTop);

            if (string.IsNullOrEmpty(input))
                continue;

            byte[] data;

            if (input[0] == '/')
            {
                string[] command = input.Split(" ");

                switch (command[0])
                {
                    case "/create":
                        CreateRoomPacket createRoomPacket = new();
                        data = PacketSerializer.Serialize(createRoomPacket);

                        break;

                    case "/enter":
                        JoinRoomPacket joinRoomPacket = new()
                        {
                            RoomId = int.Parse(command[1])
                        };
                        data = PacketSerializer.Serialize(joinRoomPacket);

                        break;

                    case "/rooms":
                        GetRoomPacket getRoomPacket = new();
                        data = PacketSerializer.Serialize(getRoomPacket);

                        break;

                    //case "/exit":
                    //    data = new byte[1];
                    //    break;

                    default:
                        continue;
                }
            }
            else if (IsJoinedRoom)
            {
                ChatMessagePacket chatPacket = new()
                {
                    Message = input
                };
                data = PacketSerializer.Serialize(chatPacket);
            }
            else
            {
                continue;
            }

            await stream.WriteAsync(data);
        }
    }
}