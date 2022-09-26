using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamOwnerChangedPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        
        public SCTeamOwnerChangedPacket(uint teamId, uint id) : base(SCOffsets.SCTeamOwnerChangedPacket, 5)
        {
            _teamId = teamId;
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_id);
            return stream;
        }
    }
}
