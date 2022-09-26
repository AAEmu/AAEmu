using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamMemberJoinedPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly TeamMember _member;
        private readonly int _party;

        public SCTeamMemberJoinedPacket(uint teamId, TeamMember member, int party) : base(SCOffsets.SCTeamMemberJoinedPacket, 5)
        {
            _teamId = teamId;
            _member = member;
            _party = party;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_member);
            stream.Write(_party);
            return stream;
        }
    }
}
