using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLootItemPacket : GamePacket
    {
        public CSLootItemPacket() : base(0x08f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var iid = stream.ReadUInt64();
            var count = stream.ReadInt32();

            _log.Warn("LootItem, IId: {0}, Count: {1}", iid, count);
        }
    }
}
