using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSummonDefendantPacket(uint trial) : GamePacket(SCOffsets.SCSummonDefendantPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(trial);
        return stream;
    }
}
