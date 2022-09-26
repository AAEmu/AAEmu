using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCheckRaceCongestionResponsePacket : GamePacket
    {
        public SCCheckRaceCongestionResponsePacket() : base(SCOffsets.SCCheckRaceCongestionResponsePacket, 5)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            for (var i = 0; i < 9; i++)
            {
                stream.Write((byte)0);
            }
            // перенаселенность рас, также смотреть ACWorlList : RaceLoad[9]
            /*RACE_CONGESTION = {
                LOW = 0,
                MIDDLE = 1,
                HIGH = 2,
                FULL = 3,
                PRE_SELECT_RACE_FULL = 9,
                CHECK = 10
            }*/
            stream.Write(true); // result
            return stream;
        }
    }
}
