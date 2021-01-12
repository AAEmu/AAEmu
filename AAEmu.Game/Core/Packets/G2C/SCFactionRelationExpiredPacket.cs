using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionRelationExpiredPacket : GamePacket
    {
        private readonly uint _id;
        private readonly uint _id2;
        private readonly byte _prevState;
        private readonly byte _currState;

        public SCFactionRelationExpiredPacket(uint id, uint id2, byte prevState, byte currState) : base(SCOffsets.SCFactionRelationExpiredPacket, 5)
        {
            _id = id;
            _id2 = id2;
            _prevState = prevState;
            _currState = currState;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_id2);
            stream.Write(_prevState);
            stream.Write(_currState);
            return stream;
        }
    }
}
