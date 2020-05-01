using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Error;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCErrorMsgPacket : GamePacket
    {
        private readonly short _errorMessage;
        private readonly uint _type;
        private readonly bool _isNotify;

        public SCErrorMsgPacket(ErrorMessageType errorMessage, uint type, bool isNotify) : base(SCOffsets.SCErrorMsgPacket, 1)
        {
            _errorMessage = (short)errorMessage; // TODO - NEED TO FIND ALL 350 IDS
            _type = type;
            _isNotify = isNotify;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_errorMessage);
            stream.Write(_errorMessage);
            stream.Write(_type);
            stream.Write(_isNotify);
            return stream;
        }
    }
}
