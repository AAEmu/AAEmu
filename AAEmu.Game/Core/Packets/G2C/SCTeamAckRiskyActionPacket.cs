using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamAckRiskyActionPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        private readonly RiskyAction _ra;
        private readonly int _w;
        private readonly short _errorMessage;

        public SCTeamAckRiskyActionPacket(uint teamId, uint id, RiskyAction ra, int w, short errorMessage) : base(SCOffsets.SCTeamAckRiskyActionPacket, 1)
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
            stream.Write((byte)_ra);
            stream.Write(_w);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}
