using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttributeConfigPacket(bool[] used) : GamePacket(SCOffsets.SCAccountAttributeConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13 (client deserializer sub_39AA3A80): used[4] (bool).
        for (var i = 0; i < 4; i++)
            stream.Write(i < used.Length && used[i]);
        return stream;
    }
}
