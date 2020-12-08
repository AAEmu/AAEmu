using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifyInGameCompletedPacket : GamePacket
    {
        public CSNotifyInGameCompletedPacket() : base(CSOffsets.CSNotifyInGameCompletedPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
          
            WorldManager.Instance.OnPlayerJoin(Connection.ActiveChar);
            _log.Info("NotifyInGameCompleted");
        }
    }
}
