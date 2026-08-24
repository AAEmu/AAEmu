using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCReadySquadPacket(ulong worldCharKey, bool ready, short errorMessage = 0)
    : GamePacket(SCOffsets.SCReadySquadPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldCharKey);
        stream.Write(ready);
        stream.Write(errorMessage);
        return stream;
    }
}
