using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTaxItemConfigPacket : GamePacket
    {
        private readonly ulong _convertRatioToAAPoint;

        public SCTaxItemConfigPacket(ulong convertRatioToAAPoint) : base(SCOffsets.SCTaxItemConfigPacket, 5)
        {
            _convertRatioToAAPoint = convertRatioToAAPoint;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_convertRatioToAAPoint);

            return stream;
        }
    }
}
