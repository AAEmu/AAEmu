using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.Proxy;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInstanceLoadedPacket : GamePacket
    {
        public CSInstanceLoadedPacket() : base(CSOffsets.CSInstanceLoadedPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            // TODO Debug
            
            Connection.SendPacket(new SCUnitStatePacket(Connection.ActiveChar));
            // Connection.SendPacket(new SCCooldownsPacket(Connection.ActiveChar));
            Connection.SendPacket(new SCDetailedTimeOfDayPacket(12f));

            Connection.ActiveChar.DisabledSetPosition = false;
            
            _log.Debug("InstanceLoaded.");
        }
    }
}
