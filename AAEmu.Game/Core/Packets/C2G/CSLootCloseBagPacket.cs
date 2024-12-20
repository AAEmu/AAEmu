using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Player closed a loot container
/// </summary>
public class CSLootCloseBagPacket() : GamePacket(CSOffsets.CSLootCloseBagPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var itemIndex = stream.ReadUInt16();
        var ownerType = (LootOwnerType)stream.ReadUInt16();
        var ownerObjId = stream.ReadBc();
        var b = stream.ReadByte();

        Logger.Warn($"LootCloseBag, itemIndex: {itemIndex}, LootOwner: {ownerType}:{ownerObjId}, b: {b}");
        
        var lootOwner = WorldManager.Instance.GetBaseUnit(ownerObjId);
        lootOwner?.LootingContainer.CloseBag(Connection.ActiveChar, itemIndex, ownerType, ownerObjId, b);
    }
}
