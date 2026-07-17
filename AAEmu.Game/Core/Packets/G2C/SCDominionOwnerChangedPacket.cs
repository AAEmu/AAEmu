using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDominionOwnerChangedPacket(ushort id, uint unkId, ulong rst, bool bestowed)
    : GamePacket(SCOffsets.SCDominionOwnerChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(unkId);
        stream.Write(rst);
        stream.Write(bestowed);
        return stream;
    }
}
