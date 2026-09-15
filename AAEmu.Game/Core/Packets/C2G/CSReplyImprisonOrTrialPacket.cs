using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReplyImprisonOrTrialPacket() : GamePacket(CSOffsets.CSReplyImprisonOrTrialPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // The client's own field name is "trial": true asks for a trial, false accepts the sentence.
        var trial = stream.ReadBoolean();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        JusticeManager.Instance.OnImprisonOrTrialReply(character, trial);
    }
}
