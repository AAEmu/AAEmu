using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
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
            var count = stream.ReadInt32();

            var objId = (uint)(iid >> 32);
            var lootDropItems = ItemManager.Instance.GetLootDropItems(objId);
            var lootDropItem = lootDropItems.Find(a => a.Id == iid);
            if (lootDropItem != null)
            {
                ItemManager.Instance.TookLootDropItem(Connection.ActiveChar, lootDropItems, lootDropItem, count);
            }
            else
            {
                if (lootDropItems.Count <= 0)
                {
                    ItemManager.Instance.RemoveLootDropItems(objId);
                    Connection.ActiveChar.BroadcastPacket(new SCLootableStatePacket(objId, false), true);
                }
            }
        }
    }
}
