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
        TrialManager.Instance.ResultIsGuilty(Connection.ActiveChar, TrialManager.Instance.GetTrial(trial), true);
    }
}
