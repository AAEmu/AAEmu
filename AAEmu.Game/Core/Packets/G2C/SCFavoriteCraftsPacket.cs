using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// by i32 craft types.
/// </remarks>
public class SCFavoriteCraftsPacket(IReadOnlyCollection<int> favoriteCraftTypes)
    : GamePacket(SCOffsets.SCFavoriteCraftsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (favoriteCraftTypes.Count > CharacterFavoriteCrafts.MaximumEntries)
            throw new InvalidOperationException("Favorite-craft count exceeds the native packet capacity.");

        stream.Write(favoriteCraftTypes.Count);
        foreach (var craftType in favoriteCraftTypes)
            stream.Write(craftType);

        return stream;
    }
}
