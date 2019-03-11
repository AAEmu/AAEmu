using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCOverHeadMarkerSetPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly int _markerIndex;
        private readonly bool _isObjId;
        private readonly uint _id;
        
        public SCOverHeadMarkerSetPacket(uint teamId, int markerIndex, bool isObjId, uint id) : base(SCOffsets.SCOverHeadMarkerSetPacket, 1)
        {
            _teamId = teamId;
            _markerIndex = markerIndex;
            _isObjId = isObjId;
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_markerIndex);
            
            stream.Write((byte)(_isObjId ? 2 : 1));
            if (_isObjId)
                stream.WriteBc(_id);
            else
                stream.Write(_id);
            return stream;
        }
    }
}
