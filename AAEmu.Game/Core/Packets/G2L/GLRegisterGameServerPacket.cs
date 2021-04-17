using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L
{
    public class GLRegisterGameServerPacket : LoginPacket
    {
        private readonly string _secretKey;
        private readonly byte _gsId;
        private readonly byte[] _additionalesGsId;

        public GLRegisterGameServerPacket(string secretKey, byte gsId, byte[] additionalesGsId) : base(GLOffsets.GLRegisterGameServerPacket)
        {
            _secretKey = secretKey;
            _gsId = gsId;
            _additionalesGsId = additionalesGsId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_secretKey);
            stream.Write(_gsId);
            stream.Write(_additionalesGsId.Length);
            foreach (var gsId in _additionalesGsId)
                stream.Write(gsId);
            return stream;
        }
    }
}
