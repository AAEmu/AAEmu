using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Sieges;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Every raid team registered for the siege the window is showing, one line per faction.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each value's name
/// alongside the value: a count, then per team the faction id (the window resolves the faction's name from
/// it), the place it holds in the list, the leader's character id (whose rank the window looks up), the
/// leader's name, whether it is the defending team, whether the war is still waiting to start, and how many
/// are registered. A longer list than the window has slots for would leave it showing teams it cannot place,
/// so it is capped.
/// </remarks>
public class SCAllSiegeRaidTeamInfoPacket(IReadOnlyList<SiegeRaidTeam> teams)
    : GamePacket(SCOffsets.SCAllSiegeRaidTeamInfoPacket, 1)
{
    /// <summary>The most teams the siege window has frames for: one defence and two offence.</summary>
    public const int MaxTeams = 3;

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(teams?.Count ?? 0, MaxTeams);
        stream.Write(count);

        for (var i = 0; i < count; i++)
        {
            var team = teams[i];
            stream.Write(team.FactionId);
            stream.Write(team.Team);
            stream.Write(team.OwnerId);
            stream.Write(team.OwnerName ?? string.Empty);
            stream.Write(team.Defense);
            stream.Write(team.IsWaitWar);
            stream.Write(team.MemberCount);
        }

        return stream;
    }
}
