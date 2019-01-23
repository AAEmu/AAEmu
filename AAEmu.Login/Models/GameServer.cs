using System.Collections.Generic;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Models
{
    public enum GSRegisterResult : byte
    {
        Success = 0,
        Error = 1,
    }

    public enum GSLoad : byte
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public class GameServer
    {
        public byte Id { get; }
        public string Name { get; }
        public string Host { get; set; }
        public ushort Port { get; set; }
        public InternalConnection Connection { get; set; }
        public bool Active => Connection != null;
        public GSLoad Load { get; set; }
        public List<byte> MirrorsId { get; set; }

        public GameServer(byte id, string name, string host, ushort port)
        {
            Id = id;
            Name = name;
            Host = host;
            Port = port;
            MirrorsId = new List<byte>();
        }

        public void SendPacket(InternalPacket packet)
        {
            Connection.SendPacket(packet);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != typeof(GameServer))
                return false;
            return Equals((GameServer) obj);
        }

        public bool Equals(GameServer other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return other.Id == Id;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var result = Id.GetHashCode();
                result = (result * 397) ^ (Connection?.GetHashCode() ?? 0);
                return result;
            }
        }
    }
}

