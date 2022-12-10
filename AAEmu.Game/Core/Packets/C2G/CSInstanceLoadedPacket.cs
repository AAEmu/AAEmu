using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInstanceLoadedPacket : GamePacket
    {
        public CSInstanceLoadedPacket() : base(CSOffsets.CSInstanceLoadedPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            // TODO Debug

            Connection.SendPacket(new SCUnitStatePacket(Connection.ActiveChar));
            // Connection.SendPacket(new SCCooldownsPacket(Connection.ActiveChar));
            var curTime = TimeManager.Instance.GetTime();
            Connection.SendPacket(new SCDetailedTimeOfDayPacket(curTime));

            Connection.ActiveChar.DisabledSetPosition = false;
            
            _log.Debug("InstanceLoaded...");
        }
    }
}
