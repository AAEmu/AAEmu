using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartedCinemaPacket : GamePacket
    {
        public CSStartedCinemaPacket() : base(0x0cb, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Warn("StartedCinema");
        }
    }
}
