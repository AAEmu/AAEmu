using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSJuryEndTestimonyPacket() : GamePacket(CSOffsets.CSJuryEndTestimonyPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var trialId = stream.ReadUInt32();
        var juryId = stream.ReadInt32();

        Logger.Info($"JuryEndTestimony, {Connection.ActiveChar.Name}, Trial: {trialId}, Jury: {juryId}");
        var trial = TrialManager.Instance.GetTrial(trialId);
        if (trial != null)
            TrialManager.JuryEndTestimony(Connection.ActiveChar, trial, juryId);
    }
}
