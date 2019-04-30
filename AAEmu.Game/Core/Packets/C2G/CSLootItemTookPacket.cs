using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLootItemTookPacket : GamePacket
    {
        public CSLootItemTookPacket() : base(0x08f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var iid = stream.ReadUInt64();
            var count = stream.ReadSByte();

            Connection.ActiveChar.SendMessage("You get to item : " + iid + ":" + count);
            Connection.ActiveChar.SendPacket(new SCLootItemTookPacket(500, iid, count));

            _log.Warn("LootItem, IId: {0}, Count: {1}", iid, count);
        }
    }
}
