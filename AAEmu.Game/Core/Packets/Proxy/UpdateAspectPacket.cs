using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class UpdateAspectPacket() : GamePacket(PPOffsets.UpdateAspectPacket, 2)
{
    // TODO Only command without body...
}
