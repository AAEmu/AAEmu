using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamRemoteMembersExPacket : GamePacket
    {
        private readonly TeamMember[] _members;
        
        public SCTeamRemoteMembersExPacket(TeamMember[] members) : base(SCOffsets.SCTeamRemoteMembersExPacket, 5)
        {
            _members = members;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_members.Length); // TODO max length 50
            foreach (var member in _members)
                member.WritePerson(stream);
            return stream;
        }
    }
}
