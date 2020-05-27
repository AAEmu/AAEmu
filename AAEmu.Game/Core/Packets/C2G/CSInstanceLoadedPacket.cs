using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.Proxy;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInstanceLoadedPacket : GamePacket
    {
        public CSInstanceLoadedPacket() : base(0x0e0, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            // TODO Debug
            
            DbLoggerCategory.Database.Connection.SendPacket(new SCUnitStatePacket(DbLoggerCategory.Database.Connection.ActiveChar));
            // Connection.SendPacket(new SCCooldownsPacket(Connection.ActiveChar));
            DbLoggerCategory.Database.Connection.SendPacket(new SCDetailedTimeOfDayPacket(12f));

            DbLoggerCategory.Database.Connection.ActiveChar.DisabledSetPosition = false;
            
            _log.Debug("InstanceLoaded.");
        }
    }
}
