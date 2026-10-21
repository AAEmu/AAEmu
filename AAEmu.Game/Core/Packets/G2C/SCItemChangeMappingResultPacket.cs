using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Result of an awakening attempt. The client turns it into
/// <c>ITEM_CHANGE_MAPPING_RESULT(result, oldGrade, oldGearScore, itemLink, bonusRate)</c>: the first
/// item supplies the old grade and gear score, the second is what the player ends up holding.
/// </summary>
/// <remarks>
/// Layout: two item structs, then the <c>item_change_mapping_groups</c> id the attempt used, then
/// the <c>result</c> byte. The client looks that group id up before it fires the result UI; sending
/// anything else (for example the pity bonus rate) makes the lookup miss and the popup never open.
/// Displayed bonus rate comes from <c>mappingFailBonus</c> on the second item's detail block
/// (<c>100 * mappingFailBonus</c>), not from a separate wire field.
/// </remarks>
public class SCItemChangeMappingResultPacket(Item oldItem, Item newItem, uint mappingGroupId, ItemChangeMappingResult result)
    : GamePacket(SCOffsets.SCItemChangeMappingResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(oldItem);
        stream.Write(newItem);
        stream.Write(mappingGroupId);
        stream.Write((byte)result);

        return stream;
    }
}
