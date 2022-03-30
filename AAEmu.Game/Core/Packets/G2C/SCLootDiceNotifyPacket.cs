using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLootDiceNotifyPacket : GamePacket
    {
        private readonly string _charName;
        private readonly Item _item;
        private readonly sbyte _dice;

        public SCLootDiceNotifyPacket(string charName,  Item item, sbyte dice) : base(SCOffsets.SCLootDiceNotifyPacket,5)
        {
            _charName = charName;
            _item = item;
            _dice = dice;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_charName);
            stream.Write(_item);
            stream.Write(_dice);

            return stream;
        }
    }
}
