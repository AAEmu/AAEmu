using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Body: u8 count, followed by count entries of { s32 id; u8 portalType; bool isFavorite }.
/// The client batches changed rows in groups of 50; the server applies the batch atomically.
/// </summary>
public sealed class CSUpdateFavoritePortalPacket() : GamePacket(CSOffsets.CSUpdateFavoritePortalPacket, 1)
{
    public FavoritePortalChange[] Changes { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        var count = stream.ReadByte();
        Changes = new FavoritePortalChange[count];
        for (var index = 0; index < Changes.Length; index++)
        {
            var id = stream.ReadInt32();
            Changes[index] = new FavoritePortalChange(
                stream.ReadByte(),
                id >= 0 ? (uint)id : uint.MaxValue,
                stream.ReadBoolean());
        }
    }

    public override void Execute()
    {
        if (Connection.ActiveChar?.Portals == null)
            return;

        if (!Connection.ActiveChar.Portals.TryUpdateFavorites(Changes))
            Logger.Warn("Rejected favorite portal update for character {0}", Connection.ActiveChar.Id);
    }
}
