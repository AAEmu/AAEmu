using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionKickToOriginResultPacket : GamePacket
    {
        private readonly string _tgtName;
        private readonly uint _id;
        private readonly uint _id2;
        private readonly short _errorMessage;

        public SCFactionKickToOriginResultPacket(string tgtName, uint id, uint id2, short errorMessage) : base(SCOffsets.SCFactionKickToOriginResultPacket, 5)
        {
            _tgtName = tgtName;
            _id = id;
            _id2 = id2;
            _errorMessage = errorMessage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tgtName);
            stream.Write(_id);
            stream.Write(_id2);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}
