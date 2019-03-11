using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamAckRiskyActionPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        private readonly byte _ra;
        private readonly int _w;
        private readonly short _errorMessage;
        
        public SCTeamAckRiskyActionPacket(uint teamId, uint id, byte ra, int w, short errorMessage) : base(SCOffsets.SCTeamAckRiskyActionPacket, 1)
        {
            _teamId = teamId;
            _id = id;
            _ra = ra;
            _w = w;
            _errorMessage = errorMessage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_id);
            stream.Write(_ra);
            stream.Write(_w);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}
