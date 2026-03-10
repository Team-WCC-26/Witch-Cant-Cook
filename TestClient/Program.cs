using System;
using System.Net.Sockets;
using System.Text;
using Protocol;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var client = new TcpClient();

        await client.ConnectAsync("127.0.0.1", 4040);

        Console.WriteLine("Connected to server");

        var stream = client.GetStream();

        // 서버 메시지 수신 스레드
        _ = Task.Run(async () =>
        {
            var buffer = new byte[1024];

            while (true)
            {
                int length = await stream.ReadAsync(buffer);

                if (length == 0)
                    break;

                var message = Encoding.UTF8.GetString(buffer, 6, length);
                Console.WriteLine(message);
            }
        });

        // 메시지 입력
        while (true)
        {
            var input = Console.ReadLine();

            if (string.IsNullOrEmpty(input))
                continue;

            var chatPacket = new ChatMessagePacket();
            chatPacket.Message = input;

            var data = PacketSerializer.Serialize(chatPacket);
            await stream.WriteAsync(data);
        }
    }
}