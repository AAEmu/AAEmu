using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAppellationGainedPacket : GamePacket
    {
        private readonly uint _appellationId;

        public SCAppellationGainedPacket(uint appellationId) : base(SCOffsets.SCAppellationGainedPacket, 5)
        {
            _appellationId = appellationId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_appellationId);
            return stream;
        }
    }
}
