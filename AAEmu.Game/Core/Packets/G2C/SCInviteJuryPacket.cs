using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInviteJuryPacket : GamePacket
    {
        private readonly string _defendantName;
        private readonly uint _trial;

        public SCInviteJuryPacket(string defendantName, uint trial) : base(SCOffsets.SCInviteJuryPacket, 5)
        {
            _defendantName = defendantName;
            _trial = trial;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_defendantName);
            stream.Write(_trial);
            return stream;
        }
    }
}
