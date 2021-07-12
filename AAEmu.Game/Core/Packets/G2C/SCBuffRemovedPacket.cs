using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBuffRemovedPacket : GamePacket
    {
        private uint _objId;
        private uint _index;

        public SCBuffRemovedPacket(uint objId, uint index) : base(SCOffsets.SCBuffRemovedPacket, 1)
        {
            _objId = objId;
            _index = index;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_index);
            return stream;
        }
        
    }
}
