using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The recruiter's own post, sent to the poster and the recruiting team once it is up. Body is one record
/// the client keeps it as
/// the team's recruit (X2Team:HasMyTeamRecruit) and draws the applicant window's header from it.
/// </summary>
public class SCRaidRecruitAddPacket(RaidRecruitRecord record) : GamePacket(SCOffsets.SCRaidRecruitAddPacket, 1)
{
    public override PacketStream Write(PacketStream stream) => RaidRecruitWire.WriteRecord(stream, record);
}
