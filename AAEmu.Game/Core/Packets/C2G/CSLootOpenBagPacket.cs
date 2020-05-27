using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

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


            ItemManager.Instance.TookLootDropItems(DbLoggerCategory.Database.Connection.ActiveChar, objId, lootAll);

        }
    }
}
