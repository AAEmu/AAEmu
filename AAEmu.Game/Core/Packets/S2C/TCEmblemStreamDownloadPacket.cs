using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCEmblemStreamDownloadPacket : StreamPacket
    {
        public TCEmblemStreamDownloadPacket() : base(0x0C)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            /*
            v2 = (char *)this;
            a2->Reader->ReadInt32("index", (char *)this + 8, 0);
            v3 = (int **)(v2 + 12);
            a2->Reader->ReadInt32("size", v2 + 12, 0);
            if ( *((_DWORD *)v2 + 3) > 3096 )
                v3 = &dword_39711528; // TODO 3096
            v4 = *v3;
            v5 = a2->Reader->field_1C();
            v6 = a2->Reader;
            savedregs = (int)v4;
            v7 = v2 + 16;
            if ( v5 )
                v6->ReadString1("data", v7, savedregs);
            else
                v6->ReadString("data", v7, savedregs);
            */
            return stream;
        }
    }
}