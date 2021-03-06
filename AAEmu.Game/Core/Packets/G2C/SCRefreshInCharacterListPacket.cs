using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRefreshInCharacterListPacket : GamePacket
    {
        public SCRefreshInCharacterListPacket() : base(SCOffsets.SCRefreshInCharacterListPacket, 5) // TODO ... SCRaceCongestionPacket?!?!?!
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            for (var i = 0; i < 9; i++) // in 1.2 ... 2.0 = 9
                stream.Write((byte) 0);
            /*
             RACE_NONE = 0,
             RACE_NUIAN = 1,
             RACE_FAIRY = 2,
             RACE_DWARF = 3,
             RACE_ELF = 4,
             RACE_HARIHARAN = 5,
             RACE_FERRE = 6,
             RACE_RETURNED = 7,
             RACE_WARBORN = 8
              */
            /*
             RACE_CONGESTION = {
                LOW = 0,
                MIDDLE = 1,
                HIGH = 2,
                FULL = 3,
                PRE_SELECT_RACE_FULL = 9,
                CHECK = 10
             }
            */
            return stream;
        }
    }
}
