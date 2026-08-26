using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Result of a regrade. The client reads it as
/// <c>GRADE_ENCHANT_RESULT(resultCode, itemLink, oldGrade, newGrade, breakRewardItemType,
/// breakRewardItemCount, breakRewardByMail)</c>.
/// </summary>
/// <remarks>
/// Layout from the 10.0.2.13 serializer : <c>result</c> (i8), the item, the
/// two grades (i8 each), then an i32 item type and <c>breakRewardItemCount</c> (u32) and
/// <c>breakRewardByMail</c> (bool). Those last three used to be missing here, which left the client
/// reading past the end of the packet for the compensation a broken item pays out.
/// </remarks>
public class SCGradeEnchantResultPacket(
    ItemGradeEnchantResult result,
    Item item,
    byte oldGrade,
    byte newGrade,
    int breakRewardItemType = 0,
    uint breakRewardItemCount = 0,
    bool breakRewardByMail = false)
    : GamePacket(SCOffsets.SCGradeEnchantResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)result);
        stream.Write(item);
        stream.Write(oldGrade);
        stream.Write(newGrade);
        stream.Write(breakRewardItemType);
        stream.Write(breakRewardItemCount);
        stream.Write(breakRewardByMail);

        return stream;
    }
}
