using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLeavedTeamPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly bool _e;
        private readonly bool _d;

        public SCLeavedTeamPacket(uint teamId, bool e, bool d) : base(SCOffsets.SCLeavedTeamPacket, 5)
        {
            _teamId = teamId;
            _e = e;
            _d = d;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_e);
            stream.Write(_d);
            return stream;
        }
    }
}
