using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitAttachedPacket : GamePacket
    {
        private readonly uint _childUnitObjId;
        private readonly byte _point;
        private readonly uint _id;
        private readonly byte _reason;

        public SCUnitAttachedPacket(uint childUnitObjId, AttachPointKind point, AttachUnitReason reason, uint id) : base(SCOffsets.SCUnitAttachedPacket, 1)
        {
            _childUnitObjId = childUnitObjId;
            _point = (byte)point;
            _reason = (byte)reason;
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
