using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    // TODO: Needs verification
    public class SCCharacterBoundPacket : GamePacket
    {
        private readonly uint _unitObjId;
        private readonly uint _returnDistrict; // Not sure what this should be
        private readonly uint _resurrectionDistrict;
        private readonly bool _returnDistrictChanged;
        private readonly uint _id;
        private readonly string _name;
        private readonly uint _zoneKey;
        private readonly Vector3 _pos;
        private readonly float _zRot;
        private readonly bool _isFavorite;

        public SCCharacterBoundPacket(uint unitObjId, uint returnDistrict, uint resurrectionDistrict,
            bool returnDistrictChanged, uint id, string name, uint zoneKey, Vector3 pos, float zRot, bool isFavorite) : 
            base(SCOffsets.SCCharacterBoundPacket, 1)
        {
            _unitObjId = unitObjId;
            _returnDistrict = returnDistrict;
            _resurrectionDistrict = resurrectionDistrict;
            _returnDistrictChanged = returnDistrictChanged;
            _id = id;
            _name = name;
            _zoneKey = zoneKey;
            _pos = pos;
            _zRot = zRot;
            _isFavorite = isFavorite;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitObjId);
            stream.Write(_returnDistrict);
            stream.Write(_resurrectionDistrict);
            stream.Write(_returnDistrictChanged);
            stream.Write(_id);
            stream.Write(_name,true);
            stream.Write(_zoneKey);
            stream.Write(_pos.X);
            stream.Write(_pos.Y);
            stream.Write(_pos.Z);
            stream.Write(_zRot);
            stream.Write(_isFavorite);
            return stream;
        }
    }
}
