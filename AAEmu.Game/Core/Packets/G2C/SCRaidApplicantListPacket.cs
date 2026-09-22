using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The recruiter's applicant window: u32 count, then count applicant rows (x2game-dev.dll FUN_39c75eb0,
/// which reads at most 100), so the writer never sends more than RaidRecruitRules.MaxApplicantsPerRecruitment.
/// </summary>
public class SCRaidApplicantListPacket(IReadOnlyList<RaidApplicantRecord> applicants)
    : GamePacket(SCOffsets.SCRaidApplicantListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(applicants.Count, RaidRecruitRules.MaxApplicantsPerRecruitment);
        stream.Write((uint)count);
        for (var i = 0; i < count; i++)
            RaidRecruitWire.WriteApplicant(stream, applicants[i]);
        return stream;
    }
}
