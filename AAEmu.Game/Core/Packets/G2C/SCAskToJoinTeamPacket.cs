using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAskToJoinTeamPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        private readonly string _name;
        private readonly bool _party;
        
        public SCAskToJoinTeamPacket(uint teamId, uint id, string name, bool party) : base(SCOffsets.SCAskToJoinTeamPacket, 1)
        {
            _teamId = teamId;
            _id = id;
            _name = name;
            _party = party;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_id);
            stream.Write(_name);
            stream.Write(_party);
            return stream;
        }
    }
}
