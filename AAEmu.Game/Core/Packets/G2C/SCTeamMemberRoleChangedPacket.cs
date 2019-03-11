using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamMemberRoleChangedPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        private readonly byte _role;
        
        public SCTeamMemberRoleChangedPacket(uint teamId, uint id, byte role) : base(SCOffsets.SCTeamMemberRoleChangedPacket, 1)
        {
            _teamId = teamId;
            _id = id;
            _role = role;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_id);
            stream.Write(_role);
            return stream;
        }
    }
}
