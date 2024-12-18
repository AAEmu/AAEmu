using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLootItemPacket() : GamePacket(CSOffsets.CSLootItemPacket, 1)
{
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
        
        // TODO: Validate arguments

        if (owner?.LootingContainer.TryTakeLoot(Connection.ActiveChar, itemIndex, null, count) ?? false)
        {
            if (owner.LootingContainer.Items.Count <= 0)
                owner.LootingContainer.UpdateLootState();
        }
    }
}
