using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamAreaInvitedPacket : GamePacket
    {
        private readonly uint _r;
        private readonly bool _s;
        
        public SCTeamAreaInvitedPacket(uint r, bool s) : base(SCOffsets.SCTeamAreaInvitedPacket, 5)
        {
            _r = r;
            _s = s;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_r);
            stream.Write(_s);
            return stream;
        }
    }
}
