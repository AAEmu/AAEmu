using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class PartialAspectPacket() : GamePacket(PPOffsets.PartialAspectPacket, 2)
{
    // TODO Only command without body...
}
