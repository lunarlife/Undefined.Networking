using System;
using System.Linq;
using Networking.DataConvert;
using Utils.Enums;

namespace Networking.Packets
{
    public abstract class Packet
    {
        private static bool _isLoaded;
        private static Enum<PacketId> _packetIds = new(); 

        public static void LoadPackets()
        {
            if (_isLoaded) throw new Exception("");
            var packetType = typeof(Packet);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.FullName))
                foreach (var t in assembly.GetTypes().Where(type => type.IsSubclassOf(packetType)).OrderBy(t => t.Name))
                    _packetIds.AddMember(t.Name, new PacketId(t));
            _isLoaded = true;
        }

        public static Type GetPacketType(ushort id) => _packetIds.Count <= id ? throw new Exception("unknown id") : _packetIds[id].Type;
        public static ushort GetPacketId(Type type) => (ushort)_packetIds[type.Name].ID;
    }
}

