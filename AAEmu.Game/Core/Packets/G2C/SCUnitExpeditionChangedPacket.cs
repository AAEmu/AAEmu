using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitExpeditionChangedPacket : GamePacket
    {
        private readonly uint _unitId;
        private readonly uint _kickerId;
        private readonly string _kicker;
        private readonly string _unitName;
        private readonly uint _id;
        private readonly uint _id2;
        private readonly bool _expel;
        
        public SCUnitExpeditionChangedPacket(uint unitId, uint kickerId, string kicker, string unitName, uint id, uint id2, bool expel) 
            : base(SCOffsets.SCUnitExpeditionChangedPacket, 1)
        {
            _unitId = unitId;
            _kickerId = kickerId;
            _kicker = kicker;
            _unitName = unitName;
            _id = id;
            _id2 = id2;
            _expel = expel;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitId);
            stream.Write(_kickerId);
            stream.Write(_kicker);
            stream.Write(_unitName);
            stream.Write(_id);
            stream.Write(_id2);
            stream.Write(_expel);
            return stream;
        }
    }
}
