using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Confirms that an item's "enchant disabled" state has been lifted. A failed awakening or a failed
/// high-scale temper can leave the item locked (<see cref="ItemGradeEnchantResult.Disable"/>); a
/// restore item clears it and the client shows the result through the same enchant alarm with
/// <see cref="ItemGradeEnchantResult.RestoreDisable"/>.
/// </summary>
/// <remarks>
/// Layout from the 10.0.2.13 serializer : the item struct followed by two
/// bytes at struct+0xe0 and +0xe1.
/// </remarks>
public class SCRestoreDisableEnchantPacket(Item item, byte type1, byte type2)
    : GamePacket(SCOffsets.SCRestoreDisableEnchantPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(item);
        stream.Write(type1);
        stream.Write(type2);

        return stream;
    }
}
