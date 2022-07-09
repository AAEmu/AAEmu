using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadOriginatorPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly uint _newOwnerId;
        private readonly uint _unknown;

        public SCDoodadOriginatorPacket(uint objId, uint newOwnerId, uint unknown) : base(SCOffsets.SCDoodadOriginatorPacket, 1)
        {
            _objId = objId;
            _newOwnerId = newOwnerId;
            _unknown = unknown;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_newOwnerId);
            stream.Write(_unknown); // UCC, Guild, Faction, something else ?

            return stream;
        }
    }
}
