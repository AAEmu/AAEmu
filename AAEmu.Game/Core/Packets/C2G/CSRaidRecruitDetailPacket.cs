using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidRecruitDetail(ownerId, createTime): u64 type (the post's owner id), i64 named expireTime by
/// the serializer but fed the row's createTime string by the binding
/// . Answered with SCRaidRecruitDetail.
/// </summary>
public class CSRaidRecruitDetailPacket() : GamePacket(CSOffsets.CSRaidRecruitDetailPacket, 1)
{
    public ulong OwnerId { get; private set; }
    public long CreateTime { get; private set; }

    public override void Read(PacketStream stream)
    {
        OwnerId = stream.ReadUInt64();
        CreateTime = stream.ReadInt64();
        RaidRecruitmentManager.Instance.Detail(Connection.ActiveChar, OwnerId, CreateTime);
    }
}
