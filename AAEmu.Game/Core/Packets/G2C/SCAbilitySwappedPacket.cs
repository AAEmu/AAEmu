using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// times over { u8 "old", u8 "new" } ×3. Reader always consumes three pairs.
/// </summary>
/// <remarks>
/// Two useful shapes:
/// <list type="bullet">
/// <item>Full triad (skillsaver): all three news valid → client updates sheet but skips
/// <c>ABILITY_CHANGED</c> (no msg_swap_ability / learn-ability banner).</item>
/// <item>Single change (NPC): <c>olds/news = [changed, General, General]</c> → one leading
/// valid news → fires <c>ABILITY_CHANGED</c>. Learn unlock uses <c>olds[0]=General</c>.</item>
/// </list>
/// Sending fewer than three pairs desyncs the SC stream ("not enough buffer for old").
/// </remarks>
public class SCAbilitySwappedPacket(
    uint objId, AbilityType[] oldAbilities, AbilityType[] newAbilities)
    : GamePacket(SCOffsets.SCAbilitySwappedPacket, 1)
{
    public const int AbilitySlots = 3;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        for (var slot = 0; slot < AbilitySlots; slot++)
        {
            stream.Write((byte)oldAbilities[slot]);
            stream.Write((byte)newAbilities[slot]);
        }

        return stream;
    }
}
