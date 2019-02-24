using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCompletedCinemaPacket : GamePacket
    {
        public CSCompletedCinemaPacket() : base(0x0ca, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Warn("CompletedCinema");
        }
    }
}
