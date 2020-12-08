using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCEmotionExpressedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly uint _objId2;
        private readonly uint _type;

        public SCEmotionExpressedPacket(uint objId, uint objId2, uint type) : base(SCOffsets.SCEmotionExpressedPacket, 1)
        {
            _objId = objId;
            _objId2 = objId2;
            _type = type;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.WriteBc(_objId2);
            stream.Write(_type);
            return stream;
        }
    }
}
