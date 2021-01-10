using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamDismissedPacket : GamePacket
    {
        private readonly uint _teamId;
        
        public SCTeamDismissedPacket(uint teamId) : base(SCOffsets.SCTeamDismissedPacket, 5)
        {
            _teamId = teamId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            return stream;
        }
    }
}
