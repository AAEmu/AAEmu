using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class BondDoodad : PacketMarshaler
    {
        private Doodad _owner;
        private readonly AttachPointKind _attachPoint;
        //private readonly byte _kind;
        private readonly int _space;
        private readonly int _spot;
        private readonly uint _animActionId;

        public uint ObjId => _owner?.ObjId ?? 0;

        public BondDoodad(AttachPointKind attachPoint, int space, int spot, uint animActionId)
        {
            _attachPoint = attachPoint;
            _space = space;
            _spot = spot;
            _animActionId = animActionId;
        }

        public BondDoodad(Doodad owner, AttachPointKind attachPoint, int space, int spot, uint animActionId)
        {
            SetOwner(owner);
            _attachPoint = attachPoint;
            _space = space;
            _spot = spot;
            _animActionId = animActionId;
        }

        public void SetOwner(Doodad owner)
        {
            _owner = owner;
        }

        public Doodad GetOwner()
        {
            return _owner;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_attachPoint);
            if ((sbyte)_attachPoint == -1)
            {
                return stream;
            }
            stream.WriteBc(_owner.ObjId);
            //stream.Write(_kind);       // remove in 3+
            stream.Write(_space);
            stream.Write(_spot);
            stream.Write(_animActionId); // added 3+

            return stream;
        }
    }
}
