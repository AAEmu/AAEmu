using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGamePointChangedPacket : GamePacket
    {
        private readonly int[,] _points;

        // TODO kind:
        // 0 - honor
        // 1 - vocation(living)

        public SCGamePointChangedPacket(int[,] points) : base(SCOffsets.SCGamePointChangedPacket, 5)
        {
            _points = points;
        }

        public override PacketStream Write(PacketStream stream)
        {
            var rows = _points.GetUpperBound(0) + 1; // количество строк (number of rows)

            stream.Write((byte)rows); // cnt
            for (var i = 0; i < rows; i++)
            {
                stream.Write((byte)_points[i, 0]); // kind
                stream.Write(_points[i, 1]); // amount
            }

            return stream;
        }
    }
}
