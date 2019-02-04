using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTEmblemStreamDownloadStatusPacket : StreamPacket
    {
        public CTEmblemStreamDownloadStatusPacket() : base(0x0F)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadInt64();
            var status = stream.ReadByte();
            var count = stream.ReadInt32();
            /*
            v3 = v2 + 24;
            a2->Reader->ReadInt32("count", v2 + 24, 0);
            v4 = 0;
            for ( i = v2 + 28; ; i += 4 )
            {
                result = 0xAA;
                if ( *(_DWORD *)v3 <= 170 )
                    result = v3;
                if ( v4 >= *(_DWORD *)result )
                    break;
                a2->Reader->ReadInt32("lost", i, 0);
                ++v4;
            }
            */
        }
    }
}