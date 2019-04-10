using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCEmblemStreamSendStatusPacket : StreamPacket
    {
        public TCEmblemStreamSendStatusPacket() : base(0x0B)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((long) 0); // type
            stream.Write((int) 0); // total
            // ----------------
            /*
            v2 = (char *)this;
            a2->Reader->ReadInt32("pat1", (char *)this, 0);
            a2->Reader->ReadInt32("pat2", v2 + 4, 0);
            a2->Reader->ReadInt32("r", v2 + 8, 0);
            a2->Reader->ReadInt32("g", v2 + 12, 0);
            a2->Reader->ReadInt32("b", v2 + 16, 0);
            a2->Reader->ReadInt32("r", v2 + 20, 0);
            a2->Reader->ReadInt32("g", v2 + 24, 0);
            a2->Reader->ReadInt32("b", v2 + 28, 0);
            v2 += 32;
            a2->Reader->ReadInt32("r", v2, 0);
            a2->Reader->ReadInt32("g", v2 + 4, 0);
            return a2->Reader->ReadInt32("b", v2 + 8, 0);
            */
            // ----------------
            stream.Write((ulong) 0); // modified
            
            stream.Write((byte) 0); // status

            return stream;
        }
    }
}