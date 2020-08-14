using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitAttachedPacket : GamePacket
    {
        private readonly uint _childUnitObjId;
        private readonly uint _objId;        // TODO UnitParent
        private readonly AttachPoint _point; // TODO UnitParent
        private readonly AttachUnitReason _reason;

        public SCUnitAttachedPacket(uint childUnitObjId, AttachPoint point, AttachUnitReason reason, uint objId) : base(SCOffsets.SCUnitAttachedPacket, 1)
        {
            _childUnitObjId = childUnitObjId;
            _point = point;
            _reason = reason;
            _objId = objId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_childUnitObjId);

            stream.Write((byte)_point);
            // if (_point != -1) - byte can't be negative anyways
            stream.WriteBc(_objId);

            stream.Write((byte)_reason);
            return stream;
        }
    }
}
