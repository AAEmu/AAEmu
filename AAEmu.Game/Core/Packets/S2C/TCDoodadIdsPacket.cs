using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCDoodadIdsPacket(int id, int nextId, int total, uint[] objIds)
    : StreamPacket(TCOffsets.TCDoodadIdsPacket)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(nextId);
        stream.Write(total);
        stream.Write(objIds.Length);
        foreach (var objId in objIds)
            stream.WriteBc(objId);

        return stream;
    }
}
