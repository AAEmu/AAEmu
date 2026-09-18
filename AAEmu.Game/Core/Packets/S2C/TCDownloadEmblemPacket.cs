using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCDownloadEmblemPacket() : StreamPacket(TCOffsets.TCDownloadEmblemPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((long)0); // type
        stream.Write(0); // size
        /*
         * Client-side handling: it takes a size at offset 8; when that size is non-zero it is capped at
         * 3072 bytes and the payload is read as the string field "emblem" at offset 12.
         * We only ever send the empty form (size 0) here.
         */
        stream.Write((ulong)0); // modified

        return stream;
    }
}
