using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCHouseFarmPacket : StreamPacket
    {
        public TCHouseFarmPacket() : base(TCOffsets.TCHouseFarmPacket)
        {
        }
        
        public override PacketStream Write(PacketStream stream)
        {
            /*
            v2 = (char *)this;
            v3 = (char *)this + 16;
            if ( a2->Reader->field_1C() )
            {
                v3[a2->Reader->ReadString1("name", v2 + 16, 128)] = 0;
            }
            else
            {
                v4 = GetStrLen(v2 + 16);
                a2->Reader->ReadString("name", v2 + 16, v4);
            }
            a2->Reader->ReadInt32("total", v2 + 8, 0);
            return a2->Reader->ReadInt32("harvestable", v2 + 12, 0);
            */
            return stream;
        }
    }
}
