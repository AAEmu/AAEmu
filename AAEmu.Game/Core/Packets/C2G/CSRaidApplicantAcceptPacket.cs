using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidApplicantAccept(charIds): u32 count, u64 type (the post's owner id, the client's own or its
/// team owner's), then count u64 character ids, at most 100, shared with
/// CSRaidApplicantReject). Each accepted applicant gets SCRaidApplicantAccept.
/// </summary>
public class CSRaidApplicantAcceptPacket() : GamePacket(CSOffsets.CSRaidApplicantAcceptPacket, 1)
{
    public ulong OwnerId { get; private set; }
    public IReadOnlyList<ulong> CharacterIds { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        var count = stream.ReadUInt32();
        OwnerId = stream.ReadUInt64();
        CharacterIds = ReadCharacterIds(stream, count);
        RaidRecruitmentManager.Instance.Accept(Connection.ActiveChar, OwnerId, CharacterIds);
    }

    internal static ulong[] ReadCharacterIds(PacketStream stream, uint count)
    {
        var size = (int)Math.Min(count, RaidRecruitRules.MaxApplicantsPerRecruitment);
        if (size > stream.LeftBytes / sizeof(ulong))
            throw new InvalidDataException("Raid applicant count exceeds the packet body.");
        var ids = new ulong[size];
        for (var i = 0; i < size; i++)
            ids[i] = stream.ReadUInt64();
        return ids;
    }
}
