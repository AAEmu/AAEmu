using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: bool myLock, bool otherLock, bool myWill.
/// 1.2 sent only the two locks, so the client read the next packet's first byte as myWill.
/// </remarks>
public class SCTradeLockUpdatePacket(bool myLock, bool otherLock, bool myWill = false) : GamePacket(SCOffsets.SCTradeLockUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(myLock);
        stream.Write(otherLock);
        stream.Write(myWill);
        return stream;
    }
}
