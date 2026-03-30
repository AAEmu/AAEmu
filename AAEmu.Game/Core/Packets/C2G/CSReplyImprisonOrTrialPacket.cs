using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReplyImprisonOrTrialPacket() : GamePacket(CSOffsets.CSReplyImprisonOrTrialPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var requestTrial = stream.ReadBoolean();

        Logger.Warn($"ReplyImprisonOrTrial, Trial: {requestTrial}");
        TrialManager.Instance.ReplyImprisonOrTrial(Connection.ActiveChar, requestTrial);
    }
}
