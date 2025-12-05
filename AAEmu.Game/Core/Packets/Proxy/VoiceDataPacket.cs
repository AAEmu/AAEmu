using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class VoiceDataPacket() : GamePacket(PPOffsets.VoiceDataPacket, 2)
{
    // TODO Only command without body...
}
