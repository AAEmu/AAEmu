using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLootDicePacket : GamePacket
    {
        private readonly Item _item;
        
        public SCLootDicePacket(Item item) : base (SCOffsets.SCLootDicePacket,1)
        {
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            return _item.Write(stream);
        }
    }
}
