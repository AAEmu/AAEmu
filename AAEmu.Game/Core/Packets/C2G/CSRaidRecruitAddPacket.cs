using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidRecruitAdd(type, subType, headcount, limitLevel, autoJoin, msg, hour, minute, limitGearPoint).
/// The body is one full record: the binding x2game-dev.dll FUN_399f1860 zeroes 0x1b0 bytes, fills those
/// nine values and serialises through FUN_39c7f4a0 / FUN_39c7ca70, so everything else in it is ignored.
/// </summary>
public class CSRaidRecruitAddPacket() : GamePacket(CSOffsets.CSRaidRecruitAddPacket, 1)
{
    public RaidRecruitPostRequest Request { get; private set; }

    public override void Read(PacketStream stream)
    {
        var record = RaidRecruitWire.ReadRecord(stream);
        Request = new RaidRecruitPostRequest(record.TypeId, record.SubTypeId, record.Headcount, record.LimitLevel,
            record.AutoJoin, record.Message ?? string.Empty, record.Hour, record.Minute, record.LimitGearPoint);
        RaidRecruitmentManager.Instance.Post(Connection.ActiveChar, Request);
    }
}
