using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidApplicantAdd(ownerId, role, createTime): u64 type (the post's owner id), u32 role
/// (TMROLE_*), i64 createTime, the row's stamp. Answered with
/// SCRaidApplicantAdd, then SCRaidApplicantAccept when the post auto-invites.
/// </summary>
public class CSRaidApplicantAddPacket() : GamePacket(CSOffsets.CSRaidApplicantAddPacket, 1)
{
    public ulong OwnerId { get; private set; }
    public uint Role { get; private set; }
    public long CreateTime { get; private set; }

    public override void Read(PacketStream stream)
    {
        OwnerId = stream.ReadUInt64();
        Role = stream.ReadUInt32();
        CreateTime = stream.ReadInt64();
        RaidRecruitmentManager.Instance.Apply(Connection.ActiveChar, OwnerId, Role, CreateTime);
    }
}
