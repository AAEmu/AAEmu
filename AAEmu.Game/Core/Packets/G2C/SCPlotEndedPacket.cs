using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPlotEndedPacket : GamePacket
    {
        private readonly ushort _tl;

        public SCPlotEndedPacket(ushort tl) : base(SCOffsets.SCPlotEndedPacket, 1)
        {
            _tl = tl;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);

            return stream;
        }
    }
}
