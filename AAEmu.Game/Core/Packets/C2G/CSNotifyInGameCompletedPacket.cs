using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers.World;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifyInGameCompletedPacket : GamePacket
    {
        public CSNotifyInGameCompletedPacket() : base(0x02a, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
          
            WorldManager.Instance.OnPlayerJoin(DbLoggerCategory.Database.Connection.ActiveChar);
            _log.Info("NotifyInGameCompleted");
        }
    }
}
