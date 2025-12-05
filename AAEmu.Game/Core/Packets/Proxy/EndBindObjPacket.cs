using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class EndBindObjPacket() : GamePacket(PPOffsets.EndBindObjPacket, 2)
{
    // TODO Only command without body...
}
