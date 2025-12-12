using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class SetAspectProfilePacket() : GamePacket(PPOffsets.SetAspectProfilePacket, 2)
{
    // TODO Only command without body...
}
