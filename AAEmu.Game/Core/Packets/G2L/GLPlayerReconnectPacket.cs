using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L
{
    public class GLPlayerReconnectPacket : LoginPacket
    {
        private readonly byte _gsId;
        private readonly uint _accountId;
        private readonly uint _connectionId;

        public GLPlayerReconnectPacket(byte gsId, uint accountId, uint connectionId) : base(GLOffsets.GLPlayerReconnectPacket)
        {
            _gsId = gsId;
            _accountId = accountId;
            _connectionId = connectionId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_gsId);
            stream.Write(_accountId);
            stream.Write(_connectionId);
            return stream;
        }
    }
}
