using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCIncreasedFavoritePortalLimitPacket() : GamePacket(SCOffsets.SCIncreasedFavoritePortalLimitPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Extra favorite-portal slots granted beyond the base limit; the reference sends 0 at world entry.
        stream.Write(0u);

        return stream;
    }
}
