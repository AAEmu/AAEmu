using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadOriginatorPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly uint _newOwnerId;
        private readonly uint _faction;

        public SCDoodadOriginatorPacket(uint objId, uint newOwnerId, uint faction) : base(SCOffsets.SCDoodadOriginatorPacket, 1)
        {
            _objId = objId;
            _newOwnerId = newOwnerId;
            _faction = faction;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_newOwnerId);
            stream.Write(_faction);

            return stream;
        }
    }
}
