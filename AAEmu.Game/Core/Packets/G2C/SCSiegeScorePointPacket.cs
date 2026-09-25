using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A zone group's siege score: how much of the guard tower's magic power each side has taken.
/// </summary>
/// <remarks>
/// The body is a zone group, then the three counters in the order outlaw, defence, offense.
/// <para>
/// The counters are the running totals, not a delta: the receiver stores what it is sent rather than adding
/// to it, and shows each against its side's win point as a percentage.
/// </para>
/// <para>
/// The first field is the zone group, and the receiver looks its dominion record up by it - a score sent for
/// any other key finds no record and changes nothing.
/// </para>
/// </remarks>
public class SCSiegeScorePointPacket(ushort zoneGroupId, uint outlawPoint, uint defensePoint, uint offensePoint)
    : GamePacket(SCOffsets.SCSiegeScorePointPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneGroupId);
        stream.Write(outlawPoint);
        stream.Write(defensePoint);
        stream.Write(offensePoint);
        return stream;
    }
}
