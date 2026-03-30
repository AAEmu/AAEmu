using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSkipFinalStatementPacket() : GamePacket(CSOffsets.CSSkipFinalStatementPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var trialId = stream.ReadUInt32();

        Logger.Debug($"SkipFinalStatement, Trial: {trialId}");
        var trial = TrialManager.Instance.GetTrial(trialId);
        if (trial != null)
            TrialManager.SkipFinalStatementReply(Connection.ActiveChar, trial);
    }
}
