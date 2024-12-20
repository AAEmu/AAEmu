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
        var u1 = stream.ReadUInt16(); // also item index?
        var u2 = stream.ReadUInt16();
        
        Logger.Warn($"LootItem, itemIndex: {itemIndex}, LootOwner: {ownerType}:{ownerObjId}, u1: {u1}, u2: {u2}");

        var owner = WorldManager.Instance.GetBaseUnit(ownerObjId);

        owner?.LootingContainer.TryTakeLoot(Connection.ActiveChar, itemIndex, null, false);
    }
}
