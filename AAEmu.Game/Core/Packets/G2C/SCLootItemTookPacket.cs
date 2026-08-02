using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// iid packing matches LootingContainer / SCLootableState:
/// (lootOwnerObjId &lt;&lt; 32) | (lootOwnerType &lt;&lt; 16) | itemIndex.
/// </summary>
public class SCLootItemTookPacket(uint itemTemplateId, ushort itemIndex, LootOwnerType lootOwnerType, uint lootOwnerId, int count)
    : GamePacket(SCOffsets.SCLootItemTookPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var iid = ((ulong)lootOwnerId << 32) | ((ulong)(ushort)lootOwnerType << 16) | itemIndex;
        stream.Write((ulong)itemTemplateId);
        stream.Write(iid);
        stream.Write((uint)count);
        return stream;
    }
}
