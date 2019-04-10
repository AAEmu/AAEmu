using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRefreshTeamMemberPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _memberId;
        private readonly uint _objId;

        public SCRefreshTeamMemberPacket(uint teamId, uint memberId, uint objId) : base(SCOffsets.SCRefreshTeamMemberPacket, 1)
        {
            _teamId = teamId;
            _memberId = memberId;
            _objId = objId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_memberId);
            stream.WriteBc(_objId);
            return stream;
        }
    }
}
