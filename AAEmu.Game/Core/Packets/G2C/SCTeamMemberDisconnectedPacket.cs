using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamMemberDisconnectedPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        private readonly TeamMember _member;

        public SCTeamMemberDisconnectedPacket(uint teamId, uint id, TeamMember member) : base(SCOffsets.SCTeamMemberDisconnectedPacket, 5)
        {
            _teamId = teamId;
            _id = id;
            _member = member;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_id);
            _member.WritePerson(stream);
            return stream;
        }
    }
}
