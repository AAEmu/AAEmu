using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRaceCongestionPacket : GamePacket
    {
        public SCRaceCongestionPacket() : base(SCOffsets.SCRaceCongestionPacket, 5)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            for (var i = 0; i < 9; i++) // in 1.2 = 9, in 1.7 = 9, in 4.0 = 9
                stream.Write((byte) 0);
            /*RACE_CONGESTION = {
                LOW = 0,
                MIDDLE = 1,
                HIGH = 2,
                FULL = 3,
                PRE_SELECT_RACE_FULL = 9,
                CHECK = 10
            }*/
            return stream;
        }
    }
}
