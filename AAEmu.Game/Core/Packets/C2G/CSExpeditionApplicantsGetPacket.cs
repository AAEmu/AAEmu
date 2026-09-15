using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSExpeditionApplicantsGetPacket() : GamePacket(CSOffsets.CSExpeditionApplicantsGetPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var rows = ExpeditionRecruitmentPacketService.Get().GetApplicantRows(Connection.ActiveChar);
        Connection.ActiveChar.SendPacket(new SCExpeditionApplicantsGetPacket(rows));
    }
}
