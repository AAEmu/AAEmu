using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// which passes each field name alongside the value:
/// int type, string memo
/// </remarks>
public class CSExpeditionApplicantAddPacket() : GamePacket(CSOffsets.CSExpeditionApplicantAddPacket, 1)
{
    public int Type { get; private set; }
    public string Memo { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadInt32();
        Memo = stream.ReadString();
        if (Type > 0 && ExpeditionRecruitmentPacketService.Get().Apply(Connection.ActiveChar, (uint)Type, Memo,
                DateTime.UtcNow) == ExpeditionRecruitmentResult.Success)
            Connection.ActiveChar.SendPacket(new SCExpeditionApplicantAddPacket(Type));
    }
}
