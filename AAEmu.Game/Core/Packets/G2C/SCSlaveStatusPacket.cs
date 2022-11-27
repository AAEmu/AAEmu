using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveStatusPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly ushort _tlId;
        private readonly int _skillCount = 0;
        private readonly int _tagCount = 0;
        private readonly string _creatorName;
        private readonly uint _ownerId;
        private readonly uint _DbHouseId;

        public SCSlaveStatusPacket(uint objId, ushort tlId, string creatorName, uint ownerId, uint DbHouseId) :
            base(SCOffsets.SCSlaveStatusPacket, 5)
        {
            _objId = objId;
            _tlId = tlId;
            _creatorName = creatorName;
            _DbHouseId = DbHouseId;
            _ownerId = ownerId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId); // bc
            stream.Write(_tlId);    // tl
            stream.Write(0ul); // type

            #region skill&tag
            stream.Write(_skillCount); // skillCount
            for (var i = 0; i < _skillCount; i++)
            {
                stream.Write(0u); // type
                stream.Write(0u); // type
                stream.Write(0u); // type
            }
            stream.Write(_tagCount); // tagCount
            for (var i = 0; i < _tagCount; i++)
            {
                stream.Write(0u); // type
                stream.Write(0u); // type
                stream.Write(0u); // type
            }
            #endregion
            stream.Write(_creatorName); // creatorName
            stream.Write(_ownerId);     // type
            stream.Write(_DbHouseId);   // dbId

            return stream;
        }
    }
}
