using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// which passes each field name alongside the value:
/// uint count, ulong type
/// </remarks>
public class CSExpeditionApplicantRejectPacket() : GamePacket(CSOffsets.CSExpeditionApplicantRejectPacket, 1)
{
    public uint Count { get; private set; }
    public IReadOnlyList<ulong> Types { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        Count = stream.ReadUInt32();
        if (Count > stream.LeftBytes / sizeof(ulong)) throw new InvalidDataException("Expedition applicant count exceeds the packet body.");
        var types = new ulong[(int)Count];
        for (var i = 0; i < types.Length; i++) types[i] = stream.ReadUInt64();
        Types = types;
        var service = ExpeditionRecruitmentPacketService.Get();
        var expeditionName = Connection.ActiveChar.Expedition?.Name ?? string.Empty;
        foreach (var type in Types.Distinct())
        {
            if (type > uint.MaxValue) continue;
            var id = (uint)type;
            if (service.Reject(Connection.ActiveChar, id) != ExpeditionRecruitmentResult.Success) continue;
            Connection.ActiveChar.SendPacket(new SCExpeditionApplicantRejectPacket(type));
            WorldManager.Instance.GetCharacterById(id)?.SendPacket(new SCExpeditionApplicantResultPacket(false,
                expeditionName));
        }
    }
}
