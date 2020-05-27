using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamBecameRaidTeamPacket : GamePacket
    {
        private readonly uint _teamId;

        public SCTeamBecameRaidTeamPacket(uint teamId) : base(SCOffsets.SCTeamBecameRaidTeamPacket, 1)
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
