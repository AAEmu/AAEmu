using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamMemberMovedPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _idFrom;
        private readonly uint _idTo;
        private readonly byte _from;
        private readonly byte _to;

        public SCTeamMemberMovedPacket(uint teamId, uint idFrom, uint idTo, byte from, byte to) : base(SCOffsets.SCTeamMemberMovedPacket, 5)
        {
            _teamId = teamId;
            _idFrom = idFrom;
            _idTo = idTo;
            _from = from;
            _to = to;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_idFrom);
            stream.Write(_idTo);
            stream.Write(_from);
            stream.Write(_to);
            return stream;
        }
    }
}
