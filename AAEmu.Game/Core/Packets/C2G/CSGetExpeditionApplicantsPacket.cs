using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSGetExpeditionApplicantsPacket : GamePacket
{
    public CSGetExpeditionApplicantsPacket() : base(CSOffsets.CSGetExpeditionApplicantsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // no body        
        Logger.Debug("GetExpeditionApplicants");

        ExpeditionManager.Instance.SendMyExpeditionApplicantsInfo(Connection.ActiveChar);
    }
}
