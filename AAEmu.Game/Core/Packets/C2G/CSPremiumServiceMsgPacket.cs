using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPremiumServiceMsgPacket : GamePacket
    {
        public CSPremiumServiceMsgPacket() : base(CSOffsets.CSPremiumServiceMsgPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSPremiumServiceMsgPacket");

            var stage = stream.ReadInt32();
            _log.Info("PremiumServieceMsg, stage {0}", stage);
            //Connection.SendPacket(new SCAccountWarnedPacket(2, "Premium ..."));
        }
    }
}
