using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailListEndPacket : GamePacket
    {
        private readonly int _totalHeaders;
        private readonly int _totalBodies;

        public SCMailListEndPacket(int totalHeaders, int totalBodies) : base(SCOffsets.SCMailListEndPacket, 1)
        {
            _totalHeaders = totalHeaders;
            _totalBodies = totalBodies;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_totalHeaders);
            stream.Write(_totalBodies);
            return stream;
        }
    }
}
