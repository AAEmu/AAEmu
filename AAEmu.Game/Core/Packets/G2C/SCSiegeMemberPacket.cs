using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One character joining or leaving a raid team in a zone group's siege.
/// </summary>
/// <remarks>
/// The body is a zone group, a team key, a character id and an add/remove flag, in that order.
/// <para>
/// Both keys are looked up rather than applied: the receiver finds its dominion record by the zone group and
/// its team by the key, and a packet whose zone group or team is not among those records changes nothing.
/// The team key is the alliance faction id, which for a defending registration is the dominion's own owner.
/// </para>
/// </remarks>
public class SCSiegeMemberPacket(ushort zoneGroupId, int teamKey, ulong characterId, bool added)
    : GamePacket(SCOffsets.SCSiegeMemberPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneGroupId);
        stream.Write(teamKey);
        stream.Write(characterId);
        stream.Write(added);
        return stream;
    }
}
