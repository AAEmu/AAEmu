using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client skillsaver slot expand. Success fires <c>SCAbilitySetSlotCountUpdated</c>
/// → client <c>ABILITY_SET_USABLE_SLOT_COUNT_CHANGED</c> toast.
/// </summary>
/// <remarks>
/// Packet has no body on 10.0.2.13.
/// </remarks>
public class CSExpandAbilitySetSlotPacket() : GamePacket(CSOffsets.CSExpandAbilitySetSlotPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        Connection.ActiveChar?.AbilitySets?.TryExpand();
    }
}
