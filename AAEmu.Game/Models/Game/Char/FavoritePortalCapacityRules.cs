using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Achievement;
using AAEmu.Game.Models.Game.Achievement.Enums;

namespace AAEmu.Game.Models.Game.Char;

public static class FavoritePortalCapacityRules
{
    public const string BaseLimitConfigKey = FavoritePortalConfigGameData.DefaultFavoritePortalLimit;

    public static int GetBaseLimit() =>
        FavoritePortalConfigGameData.Instance.RequireDefaultLimit();

    public static uint GetBonus(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var definitions = AchievementGameData.Instance.GetRecordsOfKind(
            CharRecordKind.IncreasedFavoritePortalLimit);
        return ResolveBonus(character.Records, definitions);
    }

    public static int GetLimit(Character character) =>
        CombineLimit(GetBaseLimit(), GetBonus(character));

    internal static int CombineLimit(int baseLimit, uint bonus)
    {
        if (baseLimit < 0)
            throw new InvalidOperationException($"Favorite portal base limit is negative: {baseLimit}.");

        return checked(baseLimit + checked((int)bonus));
    }

    internal static uint ResolveBonus(
        CharacterRecords characterRecords,
        IReadOnlyList<CharRecords> definitions)
    {
        ArgumentNullException.ThrowIfNull(characterRecords);
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Count != 1)
        {
            throw new InvalidOperationException(
                $"Favorite portal bonus requires exactly one record definition; found {definitions.Count}.");
        }

        var value = characterRecords.Get(definitions[0].Id);
        if (value < 0)
            throw new InvalidOperationException($"Favorite portal bonus record is negative: {value}.");

        return checked((uint)value);
    }
}
