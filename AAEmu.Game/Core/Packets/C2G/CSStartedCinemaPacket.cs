using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartedCinemaPacket : GamePacket
    {
        public CSStartedCinemaPacket() : base(CSOffsets.CSStartedCinemaPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("StartedCinema");
        }
    }
}
