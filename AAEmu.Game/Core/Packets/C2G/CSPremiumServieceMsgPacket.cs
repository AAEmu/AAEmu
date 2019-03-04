using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPremiumServieceMsgPacket : GamePacket
    {
        public CSPremiumServieceMsgPacket() : base(0x13c, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var stage = stream.ReadInt32();
            _log.Info("PremiumServieceMsg, stage {0}", stage);
            Connection.SendPacket(new SCAccountWarnedPacket(2, "Premium ..."));
        }
    }
}
