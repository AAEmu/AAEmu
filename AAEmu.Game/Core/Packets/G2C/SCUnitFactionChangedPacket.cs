using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitFactionChangedPacket : GamePacket
    {
        private readonly uint _unitId;
        private readonly string _unitName;
        private readonly uint _id;
        private readonly uint _id2;
        private readonly bool _temp;
        
        public SCUnitFactionChangedPacket(uint unitId, string unitName, uint id, uint id2, bool temp) 
            : base(SCOffsets.SCUnitFactionChangedPacket, 5)
        {
            _unitId = unitId;
            _unitName = unitName;
            _id = id;
            _id2 = id2;
            _temp = temp;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitId);
            stream.Write(_unitName);
            stream.Write(_id);
            stream.Write(_id2);
            stream.Write(_temp);
            return stream;
        }
    }
}
