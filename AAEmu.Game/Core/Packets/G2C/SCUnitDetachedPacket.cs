using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitDetachedPacket : GamePacket
    {
        private readonly uint _childUnitId;
        private readonly byte _reason;

        public SCUnitDetachedPacket(uint objId, byte reason) : base(SCOffsets.SCUnitDetachedPacket, 5)
        {
            _childUnitId = objId;
            _reason = reason;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_childUnitId);
            stream.Write(_reason);
            return stream;
        }
    }
}
