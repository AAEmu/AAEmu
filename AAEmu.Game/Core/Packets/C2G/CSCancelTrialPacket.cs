using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCancelTrialPacket() : GamePacket(CSOffsets.CSCancelTrialPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var trial = stream.ReadUInt32();
        Logger.Warn($"CancelTrial, Trial: {trial}");
        var trialData = TrialManager.Instance.GetTrial(trial);
        if (trialData.DefendantId == Connection.ActiveChar.Id)
        {
            TrialManager.Instance.ResultIsGuilty(Connection.ActiveChar, trialData, true);
        }
        else
        {
            SusManager.Instance.LogActivity(SusManager.CategoryCheating, Connection.ActiveChar, $"Player {Connection.ActiveChar.Name} tried to cancel a trial they do not belong to");
        }
    }
}
