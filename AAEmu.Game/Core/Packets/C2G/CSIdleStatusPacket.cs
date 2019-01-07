using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSIdleStatusPacket : GamePacket
    {
        public CSIdleStatusPacket() : base(0x12e, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            Connection.ActiveChar.IsOffline = stream.ReadBoolean();
            _log.Debug("IdleStatus: BcId {0}, {1}", Connection.ActiveChar.BcId, Connection.ActiveChar.IsOffline);
            Connection.ActiveChar.BroadcastPacket(
                new SCUnitIdleStatusPacket(Connection.ActiveChar.BcId, Connection.ActiveChar.IsOffline), true);
        }
    }
}