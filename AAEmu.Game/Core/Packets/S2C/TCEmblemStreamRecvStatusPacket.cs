using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCEmblemStreamRecvStatusPacket : StreamPacket
    {
        public TCEmblemStreamRecvStatusPacket() : base(0x0A)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) 0); // status
            stream.Write((int) 0); // count
            /*
            a2->Reader->ReadInt32("count", v2 + 692, 0);
            v4 = 0;
            for ( i = v2 + 12; ; i += 4 )
            {
                result = v3;
                if ( *(_DWORD *)v3 >= 170 )
                    result = "Є";
                if ( v4 >= *(_DWORD *)result )
                    break;
                a2->Reader->ReadInt32("parts", i, 0);
                ++v4;
            }
            */

            return stream;
        }
    }
}