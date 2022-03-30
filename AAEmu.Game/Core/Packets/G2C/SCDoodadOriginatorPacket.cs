using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadOriginatorPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly uint _newOwnerId;

        public SCDoodadOriginatorPacket(uint objId, uint newOwnerId) : base(SCOffsets.SCDoodadOriginatorPacket, 5)
        {
            _objId = objId;
            _newOwnerId = newOwnerId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_newOwnerId);
            stream.Write((uint)0x00000000); // UCC, Guild, Faction, something else ?

            return stream;
        }
    }
}
