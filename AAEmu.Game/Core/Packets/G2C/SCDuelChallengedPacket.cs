using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDuelChallengedPacket : GamePacket
    {
        private readonly uint _challengerId;

        public SCDuelChallengedPacket(uint challengerId) : base(SCOffsets.SCDuelChallengedPacket, 5)
        {
            _challengerId = challengerId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_challengerId);  // challengerId

            return stream;
        }
    }
}
