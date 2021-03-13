using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCDownloadEmblemPacket : StreamPacket
    {
        private readonly long _id;
        private readonly byte[] _emblem;
        private readonly ulong _modified;
        public TCDownloadEmblemPacket(long id, byte[] emblem, ulong modified) : base(0x04)
        {
            _id = id;
            _emblem = emblem;
            _modified = modified;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);   // type
            stream.Write(_emblem.Length); // size
            stream.Write(_emblem, false); // emblem, size max 3072
            /*
            if ( *(_DWORD *)(v3 + 8) > 0 )
            {
                if ( *(_DWORD *)(v3 + 8) > 3072 )
                    v4 = &dword_3971CDB0; // TODO 3072
                v5 = *v4;
                v6 = a3->Reader->field_1C() == 0;
                v7 = a3->Reader;
                if ( v6 )
                    v8 = v7->ReadString;
                else
                    v8 = (void (__stdcall *)(_DWORD, char *, int))v7->ReadString1;
                v8("emblem", (char *)(v3 + 12), v5);
            }
            */
            stream.Write(_modified); // modified

            return stream;
        }
    }
}
