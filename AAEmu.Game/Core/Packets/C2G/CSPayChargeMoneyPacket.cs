using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPayChargeMoneyPacket : GamePacket
    {
        public CSPayChargeMoneyPacket() : base(0x0a0, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            var autoUseAAPoint = stream.ReadBoolean();
            
            _log.Debug("PayChargeMoney, mailId: {0}, autoUseAAPoint: {1}", mailId, autoUseAAPoint);
            if (!MailManager.Instance.PayChargeMoney(Connection.ActiveChar, mailId, autoUseAAPoint))
                _log.Warn("PayChargeMoney failed, mailId: {0}, autoUseAAPoint: {1}", mailId, autoUseAAPoint);
        }
    }
}
