using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttributeConfigPacket(bool[] used) : GamePacket(SCOffsets.SCAccountAttributeConfigPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        for (var i = 0; i < 3; i++) // 2 in 1.2, 3 in 3+
        {
            stream.Write(used[i]);
        }
        return stream;
    }
}
