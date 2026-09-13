using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSExpeditionRecruitmentDelPacket() : GamePacket(CSOffsets.CSExpeditionRecruitmentDelPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        if (ExpeditionRecruitmentPacketService.Get().DeleteRecruitment(Connection.ActiveChar) ==
            ExpeditionRecruitmentResult.Success)
            Connection.ActiveChar.SendPacket(new SCExpeditionRecruitmentDelPacket());
    }
}
