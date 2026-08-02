using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCUpdatedFavoriteCraftsPacket(bool success) : GamePacket(SCOffsets.SCUpdatedFavoriteCraftsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(success);
        return stream;
    }
}
