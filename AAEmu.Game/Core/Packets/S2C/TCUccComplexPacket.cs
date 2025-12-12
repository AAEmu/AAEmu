using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCUccComplexPacket(Ucc ucc) : StreamPacket(TCOffsets.TCUccComplexPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)ucc.Id); // type
        stream.Write((ulong)0); // type unk
        stream.Write((ulong)0); // type unk
        stream.Write((ulong)ucc.Id); // type
        stream.Write(ucc.Modified); // modified

        return stream;
    }
}
