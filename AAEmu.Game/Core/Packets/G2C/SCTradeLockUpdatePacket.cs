using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTradeLockUpdatePacket(bool myLock, bool otherLock) : GamePacket(SCOffsets.SCTradeLockUpdatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(myLock);
        stream.Write(otherLock);
        return stream;
    }
}
