using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLootItemPacket : GamePacket
{
    public CSLootItemPacket() : base(CSOffsets.CSLootItemPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var itemIndex = stream.ReadUInt16();
        var ownerType = (LootOwnerType)stream.ReadUInt16();
        var ownerObjId = stream.ReadBc();
        var b = stream.ReadByte();
        // var iid = stream.ReadUInt64();
        var count = stream.ReadInt32();
        
        Logger.Warn($"LootItem, itemIndex: {itemIndex}, LootOwner: {ownerType}:{ownerObjId}, b: {b}, Count: {count}");

        var owner = WorldManager.Instance.GetBaseUnit(ownerObjId);
        if (owner == null)
            ownerType = LootOwnerType.None;
        
        // TODO: Validate arguments
        
        var lootDropItems = ItemManager.Instance.GetLootDropItems(ownerObjId);
        // var lootDropItem = lootDropItems.Find(a => a.Id == iid);
        var lootDropItem = lootDropItems.Find(a => a.Count > 0);
        if (lootDropItem != null)
        {
            var freeSpace = Connection.ActiveChar.Inventory.Bag.SpaceLeftForItem(lootDropItem, out _);
            if (freeSpace < lootDropItem.Count)
            {
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagFull);
                return;
            }
            ItemManager.Instance.TookLootDropItem(Connection.ActiveChar, ownerType, ownerObjId, lootDropItems, lootDropItem, count);
        }
        else
        {
            if (lootDropItems.Count <= 0)
            {
                ItemManager.Instance.RemoveLootDropItems(ownerObjId);
                Connection.ActiveChar.BroadcastPacket(new SCLootableStatePacket(ownerType, ownerObjId, false), true);
            }
        }
    }
}
