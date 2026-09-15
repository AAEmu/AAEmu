using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for the zone groups the character is a resident of, which is what the Nuon's-Arrow window
/// lists.
/// </summary>
/// <remarks>
/// packet has no body. Resident status is owning a house in a zone group, so the answer is the same
/// residency the townhall triggers report, in list form.
/// </remarks>
public class CSShowResidentZoneGroupsPacket() : GamePacket(CSOffsets.CSShowResidentZoneGroupsPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        HousingManager.Instance.ResidentZoneGroups(Connection);
    }
}
