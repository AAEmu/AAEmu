using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSetBreathPacket(uint timeLeft) : GamePacket(SCOffsets.SCSetBreathPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(timeLeft);
        return stream;
    }
}
