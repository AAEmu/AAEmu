using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// craft types, then u32 unfavoriteCraftTypeCount followed by at most 30 i32 craft types.
/// </remarks>
public class CSUpdateFavoriteCraftsPacket() : GamePacket(CSOffsets.CSUpdateFavoriteCraftsPacket, 1)
{
    public uint FavoriteCraftTypeCount { get; private set; }
    public int[] FavoriteCraftTypes { get; private set; } = [];
    public uint UnfavoriteCraftTypeCount { get; private set; }
    public int[] UnfavoriteCraftTypes { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        FavoriteCraftTypeCount = stream.ReadUInt32();
        FavoriteCraftTypes = ReadCraftTypes(stream, FavoriteCraftTypeCount);
        UnfavoriteCraftTypeCount = stream.ReadUInt32();
        UnfavoriteCraftTypes = ReadCraftTypes(stream, UnfavoriteCraftTypeCount);
    }

    public override void Execute()
    {
        var countsAreValid = FavoriteCraftTypeCount <= CharacterFavoriteCrafts.MaximumEntries &&
                             UnfavoriteCraftTypeCount <= CharacterFavoriteCrafts.MaximumEntries;
        var success = countsAreValid && Connection.ActiveChar.FavoriteCrafts.TryUpdate(
            FavoriteCraftTypes,
            UnfavoriteCraftTypes);
        Connection.SendPacket(new SCUpdatedFavoriteCraftsPacket(success));
    }

    private static int[] ReadCraftTypes(PacketStream stream, uint declaredCount)
    {
        var count = (int)Math.Min(declaredCount, CharacterFavoriteCrafts.MaximumEntries);
        var craftTypes = new int[count];
        for (var i = 0; i < craftTypes.Length; i++)
            craftTypes[i] = stream.ReadInt32();
        return craftTypes;
    }
}
