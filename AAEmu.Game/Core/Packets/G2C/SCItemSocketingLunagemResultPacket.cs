using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCItemSocketingLunagemResultPacket(byte result, ulong itemId, uint type, bool install)
    : GamePacket(SCOffsets.SCItemSocketingLunagemResultPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write(itemId);
        stream.Write(type);
        stream.Write(install);
        return stream;
    }
}
