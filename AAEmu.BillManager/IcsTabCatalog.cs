namespace AAEmu.BillManager;

/// <summary>Retail marketplace main/sub tab ids (ics_menu main_tab / sub_tab).</summary>
public static class IcsTabCatalog
{
    public static readonly (byte Id, string Name)[] MainTabs =
    [
        (1, "Main"),
        (2, "Consumables"),
        (3, "Vocations"),
        (4, "Appearance"),
        (5, "Summons"),
        (6, "Awards")
    ];

    private static readonly Dictionary<(byte Main, byte Sub), string> SubNames = new()
    {
        // Main — featured / specials
        [(1, 1)] = "Featured",
        [(1, 2)] = "Special",
        [(1, 3)] = "Limited",
        [(1, 4)] = "Sale",
        // Consumables
        [(2, 1)] = "All",
        [(2, 2)] = "Boosters",
        [(2, 3)] = "Recovery",
        [(2, 4)] = "Other",
        // Vocations
        [(3, 1)] = "All",
        [(3, 3)] = "Skill",
        [(3, 4)] = "Other",
        // Appearance
        [(4, 1)] = "All",
        [(4, 2)] = "Costume",
        [(4, 3)] = "Accessory",
        // Summons (matches in-game sidebar)
        [(5, 1)] = "All",
        [(5, 2)] = "Gliders",
        [(5, 3)] = "Mount",
        [(5, 4)] = "Battle Pet",
        // Awards
        [(6, 1)] = "All",
        [(6, 2)] = "Daily",
        [(6, 3)] = "Event",
        [(6, 5)] = "Other",
        [(6, 7)] = "Other"
    };

    public static string MainName(byte mainTab) =>
        MainTabs.FirstOrDefault(t => t.Id == mainTab).Name ?? $"Main {mainTab}";

    public static string SubName(byte mainTab, byte subTab) =>
        SubNames.TryGetValue((mainTab, subTab), out var name) ? name : $"Sub {subTab}";

    public static string TabPath(byte mainTab, byte subTab) =>
        $"{MainName(mainTab)} › {SubName(mainTab, subTab)}";

    public static IReadOnlyList<(byte Id, string Name)> SubTabsFor(byte mainTab)
    {
        var list = SubNames
            .Where(kv => kv.Key.Main == mainTab)
            .OrderBy(kv => kv.Key.Sub)
            .Select(kv => (kv.Key.Sub, kv.Value))
            .ToList();

        if (list.Count > 0)
            return list;

        return Enumerable.Range(1, 8).Select(i => ((byte)i, $"Sub {i}")).ToList();
    }
}

public sealed class TabChoice
{
    public byte Id { get; init; }
    public string Name { get; init; } = "";
    public override string ToString() => Name;
}
