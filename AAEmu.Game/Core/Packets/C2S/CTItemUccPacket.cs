using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTItemUccPacket : StreamPacket
    {
        public CTItemUccPacket() : base(0x10)
        {
        }

        public override void Read(PacketStream stream)
        {
            /*
            v2 = (char *)this;
            a2->Reader->ReadUInt32("type", (char *)this + 8, 0);
            v3 = (int *)(v2 + 240);
            a2->Reader->ReadUInt32("num", v2 + 240, 0);
            v4 = 0;
            for ( i = (int)(v2 + 16); ; i += 8 )
            {
                v6 = *v3;
                v11 = v6;
                v9 = __OFSUB__(v6, 28);
                v7 = v6 == 28;
                v8 = v6 - 28 < 0;
                result = &dword_397114E0;
                if ( (unsigned __int8)(v8 ^ v9) | v7 )
                result = &v11;
                if ( v4 >= *result )
                    break;
                a2->Reader->ReadUInt64("itemId", i, 0);
                ++v4;
            }
            return result;
            */
        }
    }
}