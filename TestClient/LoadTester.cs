using System.Net.Sockets;
using System.Collections.Concurrent;
using Protocol;
using System.Diagnostics;

namespace TestClient;

class LoadTester
{
    static int CLIENT_COUNT = 3000;

    static int connectedClients = 0;
    static int disconnectedClients = 0;

    static ConcurrentDictionary<string, long> pending = new();

    static LatencyTracker tracker = new(10000);

    // 서버에서 받은 RoomId 저장
    static ConcurrentQueue<string> roomQueue = new();

    class ClientState
    {
        public bool IsJoinedRoom = false;
    }

    static async Task Main1()
    {
        List<Task> tasks = new();

        // 절반은 생성, 절반은 입장
        for (int i = 0; i < CLIENT_COUNT; i++)
        {
            int id = i;

            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 4040);
            var stream = client.GetStream();
            PacketReceiver receiver = new();
            ClientState state = new();

            if (i % 2 == 0)
                tasks.Add(Task.Run(() => CreatorClient(id, stream, receiver, state)));
            else
                tasks.Add(Task.Run(() => JoinClient(id, stream, receiver, state)));

            await Task.Delay(5);
        }

        _ = Task.Run(Monitor);

        await Task.WhenAll(tasks);
    }

    static void AttachReceiver(NetworkStream stream, PacketReceiver receiver)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await receiver.StartAsync(stream);
            }
            catch
            {
                Interlocked.Increment(ref disconnectedClients);
            }
        });
    }

    // 🔵 방 생성 클라이언트
    static async Task CreatorClient(int id, NetworkStream stream, PacketReceiver receiver, ClientState state)
    {
        try
        {
            // RoomId 수신 처리
            receiver.OnRoomCreated = (roomId) =>
            {
                roomQueue.Enqueue(roomId);
                state.IsJoinedRoom = true;
                Console.WriteLine($"[CREATE] RoomId={roomId}");
            };

            AttachReceiver(stream, receiver);

            // 방 생성 요청
            var createPacket = new CreateRoomPacket();
            var data = PacketSerializer.Serialize(createPacket);
            await stream.WriteAsync(data);

            // 방 생성 후 채팅 대기
            await ChatLoop(stream, id, state);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Creator {id} error: {e.Message}");
            Interlocked.Increment(ref disconnectedClients);
        }
    }

    // 🟢 방 입장 클라이언트
    static async Task JoinClient(int id, NetworkStream stream, PacketReceiver receiver, ClientState state)
    {
        try
        {
            // RoomId 수신 처리
            receiver.OnRoomCreated = (roomId) =>
            {
                state.IsJoinedRoom = true;
            };

            AttachReceiver(stream, receiver);

            // RoomId 나올 때까지 대기
            string roomId;
            while (!roomQueue.TryDequeue(out roomId))
            {
                await Task.Delay(10);
            }

            Console.WriteLine($"[JOIN] Client {id} -> Room {roomId}");

            var joinPacket = new JoinRoomPacket()
            {
                RoomId = roomId
            };

            var data = PacketSerializer.Serialize(joinPacket);
            await stream.WriteAsync(data);

            await ChatLoop(stream, id, state);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Join {id} error: {e.Message}");
            Interlocked.Increment(ref disconnectedClients);
        }
    }

    // 💬 채팅 루프 (부하 핵심)
    static async Task ChatLoop(NetworkStream stream, int id, ClientState state)
    {
        while (!state.IsJoinedRoom)
        {
            await Task.Delay(10);
        }

        Interlocked.Increment(ref connectedClients);

        Random rand = new Random();

        while (true)
        {
            long timestamp = Stopwatch.GetTimestamp();
            string key = $"{id}-{timestamp}";

            pending[key] = timestamp;

            var chatPacket = new ChatMessagePacket()
            {
                Message = key
            };

            var data = PacketSerializer.Serialize(chatPacket);
            await stream.WriteAsync(data);

            // 랜덤 딜레이 (현실적인 부하)
            await Task.Delay(rand.Next(100, 500));
        }
    }

    static void RecordLatency(long ms)
    {
        tracker.Add(ms);
    }

    public static void GetChat(ChatMessagePacket packet)
    {
        string key = packet.Message;

        // 내가 보낸 메시지만 측정
        if (!pending.TryRemove(key, out long start))  return;

        long end = Stopwatch.GetTimestamp();

        double latencyMs = (end - start) * 1000.0 / Stopwatch.Frequency;

        RecordLatency((long)latencyMs);
    }

    static async Task Monitor()
    {
        while (true)
        {
            Console.WriteLine(
                $"[STATUS] Connected: {connectedClients}, " +
                $"Disconnected: {disconnectedClients}"
            );

            var (avg, max, p95) = tracker.GetStats();

            Console.WriteLine(
                $"Avg: {avg:F2} ms | Max: {max:F2} ms | P95: {p95:F2} ms"
            );

            await Task.Delay(1000);
        }
    }
}

class LatencyTracker
{
    private readonly int _maxSamples;
    private readonly Queue<long> _queue = new();
    private readonly object _lock = new();

    public LatencyTracker(int maxSamples = 10000)
    {
        _maxSamples = maxSamples;
    }

    public void Add(long latency)
    {
        lock (_lock)
        {
            _queue.Enqueue(latency);

            // 🔥 최대 개수 유지
            if (_queue.Count > _maxSamples)
                _queue.Dequeue();
        }
    }

    public (double avg, long max, long p95) GetStats()
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
                return (0, 0, 0);

            var list = new List<long>(_queue);

            list.Sort();

            long sum = 0;
            foreach (var v in list)
                sum += v;

            double avg = sum / (double)list.Count;
            long max = list[^1];

            int p95Index = (int)(list.Count * 0.95);
            long p95 = list[Math.Min(p95Index, list.Count - 1)];

            return (avg, max, p95);
        }
    }
}