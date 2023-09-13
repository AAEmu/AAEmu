using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDuelStartCountdownPacket : GamePacket
    {

        public SCDuelStartCountdownPacket() : base(SCOffsets.SCDuelStartCountdownPacket, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            //<!-- no body -->

            return stream;
        }
    }
}
