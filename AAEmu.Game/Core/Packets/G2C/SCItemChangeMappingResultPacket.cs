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
/// Layout from the 10.0.2.13 serializer (x2game.dll rva 0xab59b0): two item structs back to back
/// (the second at struct+0xe0, i.e. directly after the first), an i32 at +0x1b4 and the
/// <c>result</c> byte last. Sending the same item twice on a failure is what the client expects -
/// it still needs a valid second item to build the link from.
/// </remarks>
public class SCItemChangeMappingResultPacket(Item oldItem, Item newItem, int bonusRate, ItemChangeMappingResult result)
    : GamePacket(SCOffsets.SCItemChangeMappingResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(oldItem);
        stream.Write(newItem);
        stream.Write(bonusRate);
        stream.Write((byte)result);

        return stream;
    }
}
