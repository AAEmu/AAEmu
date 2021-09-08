using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCEmblemStreamDownloadPacket : StreamPacket
    {
        public CustomUcc _ucc;
        public int _currentIndex;
        public const ushort BufferSize = 1024 * 3 ; // 3096?
        public TCEmblemStreamDownloadPacket(Ucc ucc, int currentIndex) : base(TCOffsets.TCEmblemStreamDownloadPacket)
        {
            if (ucc is CustomUcc customUcc)
                _ucc = customUcc;
            _currentIndex = currentIndex;
        }

        public override PacketStream Write(PacketStream stream)
        {
            if ((_ucc == null) || (_ucc.Data.Count <= 0))
            {
                stream.Write((int)_currentIndex);
                stream.Write((int)0);
                stream.Write((short)0);
                return stream;
            }

            var startPos = _currentIndex * BufferSize;
            var size = Math.Min(_ucc.Data.Count - startPos, BufferSize); // 3096 is the buffer size retail seems to use
            
            stream.Write(_currentIndex);
            //stream.Write(_ucc.Data.Count); // Later versions have two size fields, one is likely for uncompressed size ?
            stream.Write(size); // Later versions have two size fields, one is likely for uncompressed size ?
            stream.Write((short)size); // Later versions have two size fields, one is likely for uncompressed size ?
            if (size > 0)
            {
                var buffer = _ucc.Data.GetRange(startPos, size).ToArray();
                stream.Write(buffer, false);
            }

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
