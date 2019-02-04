using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCItemUccDataPacket : StreamPacket
    {
        public TCItemUccDataPacket() : base(0x0D)
        {
        }
        
        public override PacketStream Write(PacketStream stream)
        {
            /*
            v2 = (char *)this;
            a2->Reader->ReadUInt32("type", (char *)this + 8, 0);
            v3 = (int *)(v2 + 12);
            a2->Reader->ReadUInt32("num", v2 + 12, 0);
            v4 = 0;
            v7 = 28;
            for ( i = (int)(v2 + 240); ; i += 8 )
            {
                result = &v7;
                if ( (unsigned int)*v3 <= 28 )
                result = v3;
                if ( v4 >= *result )
                    break;
                a2->Reader->ReadUInt64("itemId", i - 224, 0);
                a2->Reader->ReadInt64("type", i, 0);
                ++v4;
            }
            */
            return stream;
        }
    }
}