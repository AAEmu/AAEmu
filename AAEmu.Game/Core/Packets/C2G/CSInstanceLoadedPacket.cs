using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInstanceLoadedPacket : GamePacket
    {
        public CSInstanceLoadedPacket() : base(0x0dc, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO Debug
            Connection.ActiveChar.InstanceId = 2;
            Connection.ActiveChar.Position.X = 3680.518f - 14336;
            Connection.ActiveChar.Position.Y = 4572.221f - 3072;
            Connection.ActiveChar.Position.Z = 156;
            Connection.ActiveChar.Position.ZoneId = 260;
            Connection.SendPacket(new SCUnitStatePacket(Connection.ActiveChar));
            // Connection.SendPacket(new SCCooldownsPacket(Connection.ActiveChar));
            Connection.SendPacket(new SCDetailedTimeOfDayPacket(12f));
            _log.Debug("InstanceLoaded.");
        }
    }
}
