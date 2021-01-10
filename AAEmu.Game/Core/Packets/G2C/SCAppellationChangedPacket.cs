using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAppellationChangedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly uint _appellationId;

        public SCAppellationChangedPacket(uint objId, uint appellationId) : base(SCOffsets.SCAppellationChangedPacket, 5)
        {
            _objId = objId;
            _appellationId = appellationId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_appellationId);
            return stream;
        }
    }
}
