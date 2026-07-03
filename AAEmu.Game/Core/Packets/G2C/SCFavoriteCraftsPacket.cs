using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFavoriteCraftsPacket() : GamePacket(SCOffsets.SCFavoriteCraftsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Count of favorite (pinned) crafting recipes; the reference sends 0 at world entry.
        stream.Write(0u);

        return stream;
    }
}
