using System.Reflection;
using Protocol;

namespace Server
{
    public static class PacketDispatcher
    {
        private static readonly Dictionary<PacketId, Action<Session, IPacket>> _handlers = new();

        public static void RegisterAll()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();

            foreach (var type in types)
            {
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

                    Action<Session, IPacket> action = (session, packet) =>
                    {
                        method.Invoke(null, [session, packet]);
                    };

                    _handlers[attr.PacketId] = action;
                }
            }
        }

        public static void Dispatch(Session session, PacketId id, IPacket packet)
        {
            if (_handlers.TryGetValue(id, out var handler))
            {
                handler(session, packet);
            }
            else
            {
                Console.WriteLine($"Unhandled packet: {id}");
            }
        }
    }
}
