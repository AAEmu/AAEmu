using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamOfficerChangedPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly uint _id;
        private readonly bool _promote;
        
        public SCTeamOfficerChangedPacket(uint teamId, uint id, bool promote) : base(0x0d6, 1)
        {
            _teamId = teamId;
            _id = id;
            _promote = promote;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_id);
            stream.Write(_promote);
            return stream;
        }
    }
}
