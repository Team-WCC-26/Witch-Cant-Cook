using System.Collections.Concurrent;
using System.Reflection;

namespace Protocol;

public static class PacketTypeRegistry
{
    private static readonly ConcurrentDictionary<Type, PacketId> _typeToBaseId = new();

    public static PacketId GetBaseId(Type packetType)
    {
        return _typeToBaseId.GetOrAdd(packetType, static type =>
        {
            var attrs = type.GetCustomAttributes<PacketIdAttribute>(inherit: false).ToArray();

            if (attrs.Length == 0)
            {
                throw new InvalidOperationException($"Packet type {type.Name} has no [PacketId(...)] attribute.");
            }

            if (attrs.Length == 1)
            {
                return attrs[0].PacketId;
            }

            var even = attrs.Select(a => a.PacketId).FirstOrDefault(id => ((ushort)id % 2) == 0);

            if (attrs.Any(a => ((ushort)a.PacketId % 2) == 0)) return even;

            return attrs.Select(a => a.PacketId).Min();
        });
    }

    public static PacketId GetBaseId<T>() => GetBaseId(typeof(T));

    public static PacketId GetId(Type packetType, bool isServerPacket)
    {
        var baseId = GetBaseId(packetType);
        return isServerPacket && ((ushort)baseId & 1) == 0 ? (PacketId)((ushort)baseId + 1) : baseId;
    }

    public static PacketId GetId<T>(bool isServerPacket) => GetId(typeof(T), isServerPacket);

    public static bool TryGetBaseId(Type packetType, out PacketId id) => _typeToBaseId.TryGetValue(packetType, out id);
}
