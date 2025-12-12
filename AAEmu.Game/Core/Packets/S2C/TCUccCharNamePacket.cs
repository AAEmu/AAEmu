using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCUccCharNamePacket(uint id, string name) : StreamPacket(TCOffsets.TCUccCharNamePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(name);

        return stream;
    }
}
