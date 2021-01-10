using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitExpeditionChangedPacket : GamePacket
    {
        private readonly uint _unitId;
        private readonly uint _characterId;
        private readonly string _kicker;
        private readonly string _unitName;
        private readonly uint _id;
        private readonly uint _expeditionId;
        private readonly bool _expel;
        
        public SCUnitExpeditionChangedPacket(uint unitId, uint characterId, string kicker, string unitName, uint id, uint expeditionId, bool expel) 
            : base(SCOffsets.SCUnitExpeditionChangedPacket, 5)
        {
            _unitId = unitId;
            _characterId = characterId;
            _kicker = kicker;
            _unitName = unitName;
            _id = id; // TODO nation? faction?
            _expeditionId = expeditionId;
            _expel = expel;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitId);
            stream.Write(_characterId);
            stream.Write(_kicker);
            stream.Write(_unitName);
            stream.Write(_id);
            stream.Write(_expeditionId);
            stream.Write(_expel);
            return stream;
        }
    }
}
