using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class RemoveStaticObjPacket() : GamePacket(PPOffsets.RemoveStaticObjPacket, 2)
{
    // TODO Only command without body...
}
