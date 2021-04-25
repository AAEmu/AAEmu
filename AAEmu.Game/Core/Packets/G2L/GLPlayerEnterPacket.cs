using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L
{
    public class GLPlayerEnterPacket : LoginPacket
    {
        private readonly uint _connectionId;
        private readonly byte _gsId;
        private readonly byte _result;

        public GLPlayerEnterPacket(uint connectionId, byte gsId, byte result) : base(GLOffsets.GLPlayerEnterPacket)
        {
            _connectionId = connectionId;
            _gsId = gsId;
            _result = result;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_connectionId);
            stream.Write(_gsId);
            stream.Write(_result);
            return stream;
        }
    }
}
