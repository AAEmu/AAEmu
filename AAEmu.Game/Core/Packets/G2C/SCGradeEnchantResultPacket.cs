using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGradeEnchantResultPacket : GamePacket
    {
        public byte Result { get; set; }
        public Item Item { get; set; }
        public byte Type1 { get; set; }
        public byte Type2 { get; set; }

        public SCGradeEnchantResultPacket(byte result, Item item, byte type1, byte type2) : base(0x09b, 1)
        {
            Result = result;
            Item = item;
            Type1 = type1;
            Type2 = type2;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Result);
            stream.Write(Item);
            stream.Write(Type1);
            stream.Write(Type2);
            return stream;
        }
    }
}
