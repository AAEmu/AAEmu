using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Result of swapping a synthesised item's random effect. The client's "change evolving effect"
/// dialog shows the two modifiers side by side as before → after.
/// </summary>
/// <remarks>
/// Layout from the 10.0.2.13 serializer (x2game.dll rva 0xab5c40): <c>itemId</c> (i64),
/// <c>type</c> (i8), <c>changeAttr</c> (bool), then two single modifier structs - the one being
/// replaced and the one rolled in its place.
/// </remarks>
public class SCItemReRollEvolvingResultPacket(
    ulong itemId,
    byte result,
    bool changeAttr,
    ItemRndAttrUnitModifier before,
    ItemRndAttrUnitModifier after)
    : GamePacket(SCOffsets.SCItemReRollEvolvingResultPacket, 1)
{
    private static void WriteModifier(PacketStream stream, ItemRndAttrUnitModifier modifier)
    {
        modifier ??= new ItemRndAttrUnitModifier();
        stream.Write(modifier.Attribute);
        stream.Write(modifier.ModifierType);
        stream.Write(modifier.Value);
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(itemId);
        stream.Write(result);
        stream.Write(changeAttr);
        WriteModifier(stream, before);
        WriteModifier(stream, after);

        return stream;
    }
}
