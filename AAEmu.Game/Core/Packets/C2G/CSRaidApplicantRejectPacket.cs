using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidApplicantReject(charIds): the same body as CSRaidApplicantAccept (both types serialise
/// through). Each rejected applicant gets SCRaidApplicantReject.
/// </summary>
public class CSRaidApplicantRejectPacket() : GamePacket(CSOffsets.CSRaidApplicantRejectPacket, 1)
{
    public ulong OwnerId { get; private set; }
    public IReadOnlyList<ulong> CharacterIds { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        var count = stream.ReadUInt32();
        OwnerId = stream.ReadUInt64();
        CharacterIds = CSRaidApplicantAcceptPacket.ReadCharacterIds(stream, count);
        RaidRecruitmentManager.Instance.Reject(Connection.ActiveChar, OwnerId, CharacterIds);
    }
}
