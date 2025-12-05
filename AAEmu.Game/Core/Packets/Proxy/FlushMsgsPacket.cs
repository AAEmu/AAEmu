using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class FlushMsgsPacket() : GamePacket(PPOffsets.FlushMsgsPacket, 2)
{
    // TODO Only command without body...
}
