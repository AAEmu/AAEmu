using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCannotStartTradePacket : GamePacket
    {
        private readonly uint _objId;
        private readonly int _reason;

        public SCCannotStartTradePacket(uint objId, int reason) : base(SCOffsets.SCCannotStartTradePacket, 5)
        {
            _objId = objId;
            _reason = reason;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_reason);
            return stream;
        }
    }
}
