using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSExpeditionApplicantDelPacket() : GamePacket(CSOffsets.CSExpeditionApplicantDelPacket, 1)
{
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        if (TypeValue > 0 && ExpeditionRecruitmentPacketService.Get().Withdraw(Connection.ActiveChar,
                (uint)TypeValue) == ExpeditionRecruitmentResult.Success)
            Connection.ActiveChar.SendPacket(new SCExpeditionApplicantDelPacket(TypeValue));
    }
}
