using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCanStartTradePacket : GamePacket
    {
        private readonly uint _objId;

        public SCCanStartTradePacket(uint objId) : base(SCOffsets.SCCanStartTradePacket, 5)
        {
            _objId = objId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId); // uint
            return stream;
        }
    }
}
