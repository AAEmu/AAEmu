using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRejectedTeamPacket : GamePacket
    {
        private readonly string _name;
        private readonly bool _party;
        
        public SCRejectedTeamPacket(string name, bool party) : base(SCOffsets.SCRejectedTeamPacket, 5)
        {
            _name = name;
            _party = party;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_name);
            stream.Write(_party);
            return stream;
        }
    }
}
