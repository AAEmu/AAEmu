using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Result of a tempering attempt ("refurbishment" tab). The client turns this into the
/// <c>ITEM_REFURBISHMENT_RESULT(result, itemLink, beforeScale, afterScale)</c> center message.
/// </summary>
/// <remarks>
/// Field order and widths come from the 10.0.2.13 client's generated serializer
/// (x2game.dll rva 0xab5a50), which names each value as it writes it:
/// <c>result</c> (i8), the item struct, then an i32 and two i16 that the center message reads back
/// as the before/after scale. The i32 sits between the item and the two scales and is not surfaced
/// to Lua; the polish skill's own kind (1 = weapon, 2 = armor) is what fits there.
/// </remarks>
public class SCItemRefurbishmentResultPacket(ItemGradeEnchantResult result, Item item, int scaleType, short beforeScale, short afterScale)
    : GamePacket(SCOffsets.SCItemRefurbishmentResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)result);
        stream.Write(item);
        stream.Write(scaleType);
        stream.Write(beforeScale);
        stream.Write(afterScale);

        return stream;
    }
}
