using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCOtherTradeItemTookdownPacket : GamePacket
    {
        private readonly Item _item;

        public SCOtherTradeItemTookdownPacket(Item item) : base(SCOffsets.SCOtherTradeItemTookdownPacket, 5)
        {
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_item);
            return stream;
        }
    }
}
