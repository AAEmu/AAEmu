using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGradeEnchantBroadcastPacket : GamePacket
    {
        private readonly string _charName;
        private readonly byte _result;
        private readonly Item _item;
        private readonly byte _type1;
        private readonly byte _type2;

        public SCGradeEnchantBroadcastPacket(string charName, byte result, Item item, byte type1, byte type2) : base(SCOffsets.SCGradeEnchantBroadcastPacket, 5)
        {
            _charName = charName;
            _result = result;
            _item = item;
            _type1 = type1;
            _type2 = type2;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_charName);
            stream.Write(_result);
            stream.Write(_item);
            stream.Write(_type1);
            stream.Write(_type2);
            
            return stream;
        }
    }
}
