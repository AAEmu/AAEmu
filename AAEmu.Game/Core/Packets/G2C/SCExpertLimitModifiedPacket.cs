using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpertLimitModifiedPacket(bool isUpgrade, uint id, byte step)
    : GamePacket(SCOffsets.SCExpertLimitModifiedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isUpgrade);
        stream.Write(id);
        stream.Write(step);
        return stream;
    }
}
