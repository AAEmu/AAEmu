using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCDoodadIdsPacket : StreamPacket
    {
        private readonly int _id;
        private readonly int _nextId;
        private readonly int _total;
        private readonly uint[] _objIds;
        
        public TCDoodadIdsPacket(int id, int nextId, int total, uint[] objIds) : base(TCOffsets.TCDoodadIdsPacket)
        {
            _id = id;
            _nextId = nextId;
            _total = total;
            _objIds = objIds;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_nextId);
            stream.Write(_total);
            stream.Write(_objIds.Length);
            foreach (var objId in _objIds)
                stream.WriteBc(objId);

            return stream;
        }
    }
}
