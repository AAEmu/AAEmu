using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The board: u32 totalCount, u32 count, then count records, which reads at
/// most 0x32 of them), so the writer never sends more than RaidRecruitRules.ListLimit rows.
/// </summary>
public class SCRaidRecruitListPacket(int totalCount, IReadOnlyList<RaidRecruitRecord> records)
    : GamePacket(SCOffsets.SCRaidRecruitListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(records.Count, RaidRecruitRules.ListLimit);
        stream.Write((uint)totalCount);
        stream.Write((uint)count);
        for (var i = 0; i < count; i++)
            RaidRecruitWire.WriteRecord(stream, records[i]);
        return stream;
    }
}
