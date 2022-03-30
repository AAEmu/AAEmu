using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCOverHeadMarkerSetPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly OverHeadMark _index;
        private readonly bool _isObjId;
        private readonly uint _id;

        public SCOverHeadMarkerSetPacket(uint teamId, OverHeadMark index, bool isObjId, uint id) : base(SCOffsets.SCOverHeadMarkerSetPacket, 5)
        {
            _teamId = teamId;
            _index = index;
            _isObjId = isObjId;
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write((int)_index);

            stream.Write((byte)(_isObjId ? 2 : 1));
            if (_isObjId)
                stream.WriteBc(_id);
            else
                stream.Write(_id);
            return stream;
        }
    }
}
