using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLootItemPacket : GamePacket
{
    public CSLootItemPacket() : base(CSOffsets.CSLootItemPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var iid = stream.ReadUInt64();
        var count = stream.ReadInt32();

        Logger.Warn("LootItem, IId: {0}, Count: {1}", iid, count);

        var objId = (uint)(iid >> 32);
        var lootDropItems = ItemManager.Instance.GetLootDropItems(objId);
        var lootDropItem = lootDropItems.Find(a => a.Id == iid);
        if (lootDropItem != null)
        {
            var freeSpace = Connection.ActiveChar.Inventory.Bag.SpaceLeftForItem(lootDropItem, out _); 
            if (freeSpace < lootDropItem.Count)
            {
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagFull);
                return;
            }
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
