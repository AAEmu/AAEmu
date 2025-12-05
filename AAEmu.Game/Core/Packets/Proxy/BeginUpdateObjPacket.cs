using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class BeginUpdateObjPacket() : GamePacket(PPOffsets.BeginUpdateObjPacket, 2)
{
    // TODO Only command without body...
}
