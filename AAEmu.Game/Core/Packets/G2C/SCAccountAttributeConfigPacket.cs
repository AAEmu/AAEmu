using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttributeConfigPacket(bool[] used) : GamePacket(SCOffsets.SCAccountAttributeConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        for (var i = 0; i < 2; i++) // 2
        {
            stream.Write(used[i]);
        }
        return stream;
    }
}
