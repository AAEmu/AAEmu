using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class BondDoodad : PacketMarshaler
    {
        private Doodad _owner;
        private readonly byte _attachPoint;
        //private readonly byte _kind; // deleted in 1.7
        private readonly int _space;
        private readonly int _spot;
        private readonly uint _animActionId; // added in 1.7

        public uint ObjId => _owner?.ObjId ?? 0;

        public BondDoodad(byte attachPoint, int space, int spot, uint animActionId)
        {
            _attachPoint = attachPoint;
            //_kind = kind;
            _space = space;
            _spot = spot;
            _animActionId = animActionId;
        }

        public BondDoodad(Doodad owner, byte attachPoint, int space, int spot, uint animActionId)
        {
            SetOwner(owner);
            _attachPoint = attachPoint;
            //_kind = kind;
            _space = space;
            _spot = spot;
            _animActionId = animActionId;
        }

        public void SetOwner(Doodad owner)
        {
            _owner = owner;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_attachPoint);
            if ((sbyte)_attachPoint == -1)
            {
                return stream;
            }
            stream.WriteBc(_owner.ObjId);
            //stream.Write(_kind);       // deleted in 1.7
            stream.Write(_space);
            stream.Write(_spot);
            stream.Write(_animActionId); // added in 1.7

            return stream;
        }
    }
}
