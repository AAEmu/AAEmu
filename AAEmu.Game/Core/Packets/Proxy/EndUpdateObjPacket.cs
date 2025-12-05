using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class EndUpdateObjPacket() : GamePacket(PPOffsets.EndUpdateObjPacket, 2)
{
    // TODO Only command without body...
}
