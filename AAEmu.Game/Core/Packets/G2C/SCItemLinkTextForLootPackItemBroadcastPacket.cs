using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCItemLinkTextForLootPackItemBroadcastPacket : GamePacket
    {
        private readonly string _charName;
        private readonly Item _item1;
        private readonly Item _item2;

        public SCItemLinkTextForLootPackItemBroadcastPacket(string charName,  Item item1,  Item item2) : base(SCOffsets.SCItemLinkTextForLootPackItemBroadcastPacket,5)
        {
            _charName = charName;
            _item1 = item1;
            _item2 = item2;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_charName);
            stream.Write(_item1);
            stream.Write(_item2);

            return stream;
        }
    }
}
