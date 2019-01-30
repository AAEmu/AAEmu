using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCDoodadIdsPacket : StreamPacket
    {
        public TCDoodadIdsPacket() : base(0x03)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((int) 0); // id
            stream.Write((int) 0); // next
            stream.Write((int) 0); // total
            stream.Write((int) 0); // count
            /*
            result = a3->Reader->ReadInt32("count", v3 + 20, 0);
            v9 = 0;
            if ( *((_DWORD *)v3 + 5) > 0 )
            {
                v5 = v3 + 24;
                do
                {
                    if ( ((unsigned __int8 (__stdcall *)(const char *, signed int))a3->Reader->field_14)("did", 1) )
                    {
                        if ( a3->Reader->field_1C() )
                            *v5 = 0;
                        v6 = a3->Reader->field_1C();
                        v7 = a3->Reader;
                        if ( v6 )
                            v8 = v7->ReadBytes1;
                        else
                            v8 = v7->ReadBytes;
                        v8(a3, "bc", v5, 3);
                        ((void (__thiscall *)(struc_1 *))a3->Reader->field_18)(a3);
                    }
                    result = v9 + 1;
                    ++v5;
                    v9 = result;
                }
                while ( result < *((_DWORD *)v3 + 5) );
            }
            */
            return stream;
        }
    }
}