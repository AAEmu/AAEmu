using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSPremiumServiceMsgPacket() : GamePacket(CSOffsets.CSPremiumServiceMsgPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var stage = stream.ReadInt32();
        Logger.Info("PremiumServieceMsg, stage {0}", stage);
        // NOTE: previously replied with SCAccountWarnedPacket(2,"Premium...") — that is the wrong response AND
        // its 1.2 body (source + msg only) is too short for the 10.0.2.13 SCAccountWarned layout, so the client
        // overran reading "countdownTime" → "sc error; not enough buffer for countdownTime" → SC message-count
        // desync → char-select broke (create disabled). The client needs no reply here, so don't send one.
    }
}
