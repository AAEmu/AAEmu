using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifyInGameCompletedPacket : GamePacket
    {
        public CSNotifyInGameCompletedPacket() : base(CSOffsets.CSNotifyInGameCompletedPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            if (Connection.ActiveChar.Family > 0) {
                FamilyManager.Instance.OnCharacterLogin(Connection.ActiveChar);
            }
            Connection.ActiveChar.DisabledSetPosition = false;
            Connection.ActiveChar.Mails.OpenMailbox();
            //TODO Ideally OpenMailbox will be moved to login so its only called once. 
            _log.Info("NotifyInGameCompleted");
        }
    }
}
