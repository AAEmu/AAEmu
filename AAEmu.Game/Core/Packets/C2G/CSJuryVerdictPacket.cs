using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSJuryVerdictPacket() : GamePacket(CSOffsets.CSJuryVerdictPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var trialId = stream.ReadUInt32();
        var jury = stream.ReadInt32();
        var sentence = stream.ReadByte();

        Logger.Info($"JuryVerdict, {Connection.ActiveChar.Name}, Trial: {trialId}, Jury: {jury}, Sentence: {sentence}");
        var trial = TrialManager.Instance.GetTrial(trialId);
        if (trial != null)
            TrialManager.JuryVerdict(Connection.ActiveChar, trial, jury, sentence);
    }
}
