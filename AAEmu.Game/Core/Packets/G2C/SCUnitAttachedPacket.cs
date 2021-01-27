using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitAttachedPacket : GamePacket
    {
        private readonly uint _childUnitObjId;
        private readonly byte _point;
        private readonly uint _id;
        private readonly byte _reason;

        public SCUnitAttachedPacket(uint childUnitObjId, byte point, byte reason, uint id) : base(SCOffsets.SCUnitAttachedPacket, 5)
        {
            _childUnitObjId = childUnitObjId;
            _point = point;
            _reason = reason;
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_childUnitObjId);

            stream.Write(_point);
            // if (_point != -1) - byte can't be negative anyways
            stream.WriteBc(_id);

            stream.Write(_reason);
            return stream;
        }
    }
}
