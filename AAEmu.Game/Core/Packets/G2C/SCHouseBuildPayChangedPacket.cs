using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseBuildPayChangedPacket(ushort tl, int moneyAmount)
    : GamePacket(SCOffsets.SCHouseBuildPayChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(moneyAmount);
        return stream;
    }
}
