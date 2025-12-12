using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class BeginBindObjPacket() : GamePacket(PPOffsets.BeginBindObjPacket, 2)
{
    // TODO Only command without body...
}
