using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Confirms an application to the applicant with the post it went to. Same one-record body as
/// SCRaidRecruitAdd; the client files it in its application map, which drives the
/// myApplicant and fullApplicant flags and the RAID_RECRUIT_HUD alarm.
/// </summary>
public class SCRaidApplicantAddPacket(RaidRecruitRecord record) : GamePacket(SCOffsets.SCRaidApplicantAddPacket, 1)
{
    public override PacketStream Write(PacketStream stream) => RaidRecruitWire.WriteRecord(stream, record);
}
