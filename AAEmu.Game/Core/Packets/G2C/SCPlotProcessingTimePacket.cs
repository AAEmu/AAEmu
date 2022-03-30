using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPlotProcessingTimePacket : GamePacket
    {
        private readonly uint _plotId;
        private readonly ulong _processingTime;

        public SCPlotProcessingTimePacket(uint plotId, ulong processingTime) : base(SCOffsets.SCPlotProcessingTimePacket, 5)
        {
            _plotId = plotId;
            _processingTime = processingTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_plotId); // type
            stream.Write(_processingTime);
            return stream;
        }
    }
}
