using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAskToJoinTeamPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _senderId;
        private readonly string _senderName;
        private readonly bool _isParty;
        
        public SCAskToJoinTeamPacket(uint teamId, uint senderId, string senderName, bool isParty) : base(SCOffsets.SCAskToJoinTeamPacket, 1)
        {
            _teamId = teamId;
            _senderId = senderId;
            _senderName = senderName;
            _isParty = isParty;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_senderId);
            stream.Write(_senderName);
            stream.Write(_isParty);
            return stream;
        }
    }
}
