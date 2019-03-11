using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRefreshTeamMemberPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        private readonly uint _unitId;

        public SCRefreshTeamMemberPacket(uint teamId, uint id, uint unitId) : base(SCOffsets.SCRefreshTeamMemberPacket, 1)
        {
            _teamId = teamId;
            _id = id;
            _unitId = unitId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_id);
            stream.WriteBc(_unitId);
            return stream;
        }
    }
}
