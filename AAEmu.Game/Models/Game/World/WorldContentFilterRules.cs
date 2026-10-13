namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Which <c>world_contents.name</c> values belong in the active filter pack.
/// Names in the pack are treated as blocks by the client (e.g. create-race list).
/// </summary>
public static class WorldContentFilterRules
{
    /// <summary>
    /// Shipped compact tags dwarf/warborn character rows under this name. Putting it in the pack
    /// removes those races from <c>GetRaceList</c> even when <c>characters.creatable</c> is true.
    /// </summary>
    public const string CharacterTestFilterName = "character_test";

    /// <summary>False means omit the name from <c>SCWorldContent</c>.</summary>
    public static bool IncludeFilterName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return !string.Equals(name, CharacterTestFilterName, StringComparison.Ordinal);
    }
}
