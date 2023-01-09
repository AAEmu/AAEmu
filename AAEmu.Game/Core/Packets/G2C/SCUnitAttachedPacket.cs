using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitAttachedPacket : GamePacket
    {
        private readonly uint _childUnitObjId;
        private readonly byte _point;
        private readonly uint _objId;
        private readonly byte _reason;

        public SCUnitAttachedPacket(uint childUnitObjId, AttachPointKind point, AttachUnitReason reason, uint objId)
            : base(SCOffsets.SCUnitAttachedPacket, 5)
        {
            _childUnitObjId = childUnitObjId;
            _point = (byte)point;
            _reason = (byte)reason;
            _objId = objId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_childUnitObjId);
            stream.Write(_point);
            if (_point != 255)
            {
                stream.WriteBc(_objId);
            }
            stream.Write(_reason);

            return stream;
        }
    }
}
