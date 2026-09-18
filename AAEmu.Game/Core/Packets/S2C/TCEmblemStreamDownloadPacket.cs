using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCEmblemStreamDownloadPacket : StreamPacket
{
    public CustomUcc _ucc;
    public int _currentIndex;
    public const ushort BufferSize = 1024 * 3; // 3096?
    public TCEmblemStreamDownloadPacket(Ucc ucc, int currentIndex) : base(TCOffsets.TCEmblemStreamDownloadPacket)
    {
        if (ucc is CustomUcc customUcc)
            _ucc = customUcc;
        _currentIndex = currentIndex;
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (_ucc == null || _ucc.Data.Count <= 0)
        {
            stream.Write(_currentIndex);
            stream.Write(0);
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
         * Client-side layout: int32 "index" at +8, int32 "size" at +12, then the "data" payload at +16.
         * A size above 3096 is clamped to the 3096-byte buffer before the payload is read.
         */
        return stream;
    }
}
