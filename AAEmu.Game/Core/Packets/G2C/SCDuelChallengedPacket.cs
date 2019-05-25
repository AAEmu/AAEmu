using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDuelChallengedPacket : GamePacket
    {
        private readonly uint _challengedId;

        public SCDuelChallengedPacket(uint challengedId) : base(SCOffsets.SCDuelChallengedPacket, 1)
        {
            _challengedId = challengedId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_challengedId);  // challengerId

            return stream;
        }
    }
}
