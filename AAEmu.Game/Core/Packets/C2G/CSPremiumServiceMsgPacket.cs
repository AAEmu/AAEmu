using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSPremiumServiceMsgPacket : GamePacket
{
    public CSPremiumServiceMsgPacket() : base(CSOffsets.CSPremiumServiceMsgPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSPremiumServiceMsgPacket");

        var stage = stream.ReadInt32();
        Logger.Info("PremiumServieceMsg, stage {0}", stage);
        switch (stage)
        {
            case 1:
                Connection.SendPacket(new SCAccountWarnedCodePacket(1, 0,""));
                break;
            case 2:
                Connection.SendPacket(new SCAccountWarnedCodePacket(2, 7, ""));
                break;
            case 3:
                Connection.SendPacket(new SCAccountWarnedCodePacket(2, 0, "Premium ..."));
                break;
            default:
                break;
        }
    }
}
