using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamMemberLeavedPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        private readonly bool _e;

        public SCTeamMemberLeavedPacket(uint teamId, uint id, bool e) : base(SCOffsets.SCTeamMemberLeavedPacket, 5)
        {
            _teamId = teamId;
            _id = id;
            _e = e;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_id);
            stream.Write(_e);
            return stream;
        }
    }
}
