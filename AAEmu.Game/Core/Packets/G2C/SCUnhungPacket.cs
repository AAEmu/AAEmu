using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnhungPacket : GamePacket
    {
        private readonly uint _unitObjId;
        private readonly uint _targetObjId;
        private readonly uint _reason;

        public SCUnhungPacket(uint unitObjId, uint targetObjId, uint reason) : base(SCOffsets.SCUnhungPacket, 1)
        {
            _unitObjId = unitObjId;
            _targetObjId = targetObjId;
            _reason = reason;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitObjId);
            stream.WriteBc(_targetObjId);
            stream.Write(_reason);
            return stream;
        }
    }
}
