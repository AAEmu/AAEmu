using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L
{
    public class GLRegisterGameServerPacket : LoginPacket
    {
        private readonly byte _gsId;
        private readonly string _ip;
        private readonly ushort _port;

        public GLRegisterGameServerPacket(byte gsId, string ip, ushort port) : base(0x00)
        {
            _gsId = gsId;
            _ip = ip;
            _port = port;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_gsId);
            stream.Write(_ip);
            stream.Write(_port);
            return stream;
        }
    }
}
