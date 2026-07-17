using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGamePointChangedPacket(byte kind, int amount) : GamePacket(SCOffsets.SCGamePointChangedPacket, 5)
{
    // TODO kind:
    // 0 - honor
    // 1 - vocation(living)

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(kind);
        stream.Write(amount);
        return stream;
    }
}
