using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCOtherTradeItemPutupPacket : GamePacket
    {
        private readonly Item _item;

        public SCOtherTradeItemPutupPacket(Item item) : base(SCOffsets.SCOtherTradeItemPutupPacket, 5)
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
