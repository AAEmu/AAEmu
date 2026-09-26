using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The join dialog's data for one post,, the answer to CSRaidRecruitDetail;
/// the client raises RAID_RECRUIT_DETAIL and opens ShowRaidJoiningDialog with it.
/// </summary>
public class SCRaidRecruitDetailPacket(RaidRecruitRecord record) : GamePacket(SCOffsets.SCRaidRecruitDetailPacket, 1)
{
    public override PacketStream Write(PacketStream stream) => RaidRecruitWire.WriteDetail(stream, record);
}
