using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUploadEmblemStreamPacket : StreamPacket
    {
        public CTUploadEmblemStreamPacket() : base(0x0C)
        {
        }

        public override void Read(PacketStream stream)
        {
            var total = stream.ReadInt32();
            var size = stream.ReadInt32();
            var index = stream.ReadInt32();
            var data = stream.ReadString(); // or bytes; max length 3096

            /*
            v2 = (char *)this;
            a2->Reader->ReadInt32("total", (char *)this + 8, 0);
            v3 = (int **)(v2 + 12);
            a2->Reader->ReadInt32("size", v2 + 12, 0);
            a2->Reader->ReadInt32("index", v2 + 16, 0);
            if ( *((_DWORD *)v2 + 3) > 3096 )
                v3 = &dword_39711528;
            v4 = *v3;
            v5 = a2->Reader->field_1C();
            savedregs = (int)v4;
            v8 = v2 + 20;
            v6 = v5 == 0;
            v7 = a2->Reader;
            if ( v6 )
                v7->ReadString("data", v8, savedregs);
            else
                v7->ReadString1("data", v8, savedregs);
            */
        }
    }
}