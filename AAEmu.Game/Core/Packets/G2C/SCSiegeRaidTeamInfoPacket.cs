using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One faction's raid team for a siege: who leads it, which raid zone it fights in, and its members.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each value's name
/// alongside the value: the leader's character id, the raid zone the client names from its own list of the
/// four siege territories, the row count, then per member the character id (the client takes the commander's
/// name from the row whose id matches the leader's), the name, the level, the heir level, the three ability
/// ids and the gear score. A leader id the rows do not carry leaves the commander's name empty, the same as an
/// empty owner on the team list.
/// </remarks>
public class SCSiegeRaidTeamInfoPacket(
    ulong leaderId,
    ushort raidZoneId,
    IReadOnlyList<SiegeRaidTeamMemberInfo> members)
    : GamePacket(SCOffsets.SCSiegeRaidTeamInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var rows = members ?? [];
        stream.Write(leaderId);
        stream.Write(raidZoneId);
        stream.Write(rows.Count);

        foreach (var member in rows)
        {
            stream.Write(member.CharacterId);
            stream.Write(member.Name ?? string.Empty);
            stream.Write(member.Level);
            stream.Write(member.HeirLevel);
            stream.Write(member.Ability1);
            stream.Write(member.Ability2);
            stream.Write(member.Ability3);
            stream.Write(member.GearScore);
        }

        return stream;
    }
}

/// <summary>
/// One member of a raid team as the member list shows them: who they are, what they are and how well they are
/// geared.
/// </summary>
public readonly record struct SiegeRaidTeamMemberInfo(
    ulong CharacterId,
    string Name,
    byte Level,
    byte HeirLevel,
    byte Ability1,
    byte Ability2,
    byte Ability3,
    uint GearScore);
