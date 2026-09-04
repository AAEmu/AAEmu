using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Skillsaver save/activate/delete result. Client maps <c>responseType</c> to
/// <c>ABILITY_SET_CHANGED</c> (1=saved_job, 2=changed_job, 3=deleted_job; ≤0 = lack_of_saved_job_slot).
/// </summary>
/// <remarks>
/// Wire layout (no leading pad): sbyte responseType, u32 slotIndex, sbyte usedFreeActivationCount.
/// An earlier recovered "unnamed1" pad made the client treat 0 as responseType and always toast failure.
/// </remarks>
public class SCAbilitySetUpdatedPacket(sbyte responseType, uint slotIndex, sbyte usedFreeActivationCount)
    : GamePacket(SCOffsets.SCAbilitySetUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(responseType);
        stream.Write(slotIndex);
        stream.Write(usedFreeActivationCount);
        return stream;
    }
}
