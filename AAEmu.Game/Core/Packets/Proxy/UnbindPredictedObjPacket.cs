using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class UnbindPredictedObjPacket() : GamePacket(PPOffsets.UnbindPredictedObjPacket, 2)
{
    // TODO Only command without body...
}
