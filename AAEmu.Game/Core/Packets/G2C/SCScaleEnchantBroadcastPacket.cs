using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Server-wide notice that someone tempered an item. The client renders it through
/// <c>SCALE_ENCHANT_BROADCAST(characterName, resultCode, itemLink, oldScale, newScale)</c>.
/// </summary>
/// <remarks>
/// Layout from the 10.0.2.13 serializer : <c>charName</c> (string, 0x80
/// cap), <c>result</c> (i8), the item struct, then the two i16 scales. Same shape as
/// <see cref="SCGradeEnchantBroadcastPacket"/> except the trailing pair is 16-bit.
/// </remarks>
public class SCScaleEnchantBroadcastPacket(string charName, ItemGradeEnchantResult result, Item item, short oldScale, short newScale)
    : GamePacket(SCOffsets.SCScaleEnchantBroadcastPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(charName);
        stream.Write((byte)result);
        stream.Write(item);
        stream.Write(oldScale);
        stream.Write(newScale);

        return stream;
    }
}
