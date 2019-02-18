using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGradeEnchantBroadcastPacket : GamePacket
    {
        public SCGradeEnchantBroadcastPacket() : base(0x09e, 1)
        {

        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write("Lemes"); // charName
            stream.Write((byte)1); // result
            stream.Write(new Item(20209, ItemManager.Instance.GetTemplate(20209), 1)); // item
            stream.Write((byte)1); // type
            stream.Write((byte)1); // type
            return stream;
        }
    }
}
