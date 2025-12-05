using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCUccComplexCheckValidPacket(ulong type, bool isValid)
    : StreamPacket(TCOffsets.TCUccComplexCheckValidPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(type);
        stream.Write(isValid);

        return stream;
    }
}
