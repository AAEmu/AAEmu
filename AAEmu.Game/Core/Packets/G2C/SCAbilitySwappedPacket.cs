using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// times over { u8 "old", u8 "new" } — one pair per ability slot, not just the slot that changed.
/// Sending a single pair left the client short by four bytes ("not enough buffer for old"), which
/// desynced the SC stream and silently dropped the swap, so unlocking a second skillset did nothing.
/// </summary>
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
