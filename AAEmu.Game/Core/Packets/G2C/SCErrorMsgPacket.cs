using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCErrorMsgPacket : GamePacket
    {
        private readonly ErrorMessageType _errorMessage;
        private readonly uint _type;
        private readonly bool _isNotify;

        public SCErrorMsgPacket(ErrorMessageType errorMessage, uint type, bool isNotify) : base(SCOffsets.SCErrorMsgPacket, 5)
        {
            _errorMessage = errorMessage;
            _type = type;
            _isNotify = isNotify;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((ushort)_errorMessage);
            stream.Write((ushort)_errorMessage);
            stream.Write(_type);
            stream.Write(_isNotify);
            return stream;
        }
    }
}
