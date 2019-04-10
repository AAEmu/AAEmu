using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLootOpenBagPacket : GamePacket
    {
        public CSLootOpenBagPacket() : base(0x08e, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var obj2Id = stream.ReadBc();
            var lootAll = stream.ReadBoolean();

            _log.Warn("LootOpenBag, ObjId: {0}, LootAll: {1}", objId, lootAll);
        }
    }
}
