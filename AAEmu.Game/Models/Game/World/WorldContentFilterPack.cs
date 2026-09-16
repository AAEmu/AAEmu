using System.Text;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Builds the world-content pack the client parses on entering the world (<c>SCWorldContentPacket</c>).
/// The client reads it as a count of content categories, each with the names that are configured for it,
/// and stores the matches in the per-category filter its features consult.
/// </summary>
/// <remarks>
/// Layout, taken from the client's own reader: <c>u16</c> category count, then per category a <c>u8</c>
/// name length with the category's name and a <c>u16</c> entry count, then per entry a <c>u8</c> name
/// length with the name. There is **no id on the wire**: the client matches the group by the category
/// name alone, against its own registration table, so the names sent have to be its spellings.
/// </remarks>
public static class WorldContentFilterPack
{
    /// <summary>Category names as the client numbers them. The content table spells them differently.</summary>
    private static readonly string[] CategoryNames =
    [
        "craft",           // 0
        "zone",            // 1
        "tower_def",       // 2
        "game_schedule",   // 3
        "transfer",        // 4
        "npc_spawner",     // 5
        "doodad",          // 6
        "quest",           // 7
        "character",       // 8
        "loot_pack",       // 9
        "appellation_merit", // 10
        "schedule_item",   // 11
        "arche_pass",      // 12
        "instance",        // 13
        "appellation",     // 14
        "achievement",     // 15
        "tagged_buff",     // 16
        "festival_zone"    // 17
    ];

    public static int CategoryCount => CategoryNames.Length;

    /// <summary>The client's own spelling of a category id — the only thing the wire carries for it.</summary>
    public static string CategoryNameOf(byte categoryId)
    {
        return categoryId < CategoryNames.Length ? CategoryNames[categoryId] : null;
    }

    /// <summary>
    /// Content type names in the table that are not merely the client's name without its underscores.
    /// The table stores quest contexts, and the client's only quest-shaped category is <c>quest</c>.
    /// </summary>
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.Ordinal)
    {
        ["questcontext"] = "quest"
    };

    /// <summary>
    /// The client's id for a content type, matched on letters and digits alone: the table writes the
    /// categories without underscores (<c>QuestContext</c>, <c>GameSchedule</c>) while the client's own
    /// names carry them.
    /// </summary>
    public static byte? CategoryIdOf(string categoryName)
    {
        var normalized = Normalize(categoryName);
        if (normalized.Length == 0)
            return null;

        if (TypeAliases.TryGetValue(normalized, out var aliased))
            normalized = Normalize(aliased);

        for (var index = 0; index < CategoryNames.Length; index++)
        {
            if (Normalize(CategoryNames[index]) == normalized)
                return (byte)index;
        }

        return null;
    }

    /// <summary>Letters and digits only, lower case — the form both spellings agree on.</summary>
    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Serializes one group per category, in the client's category order, each carrying its own names.
    /// Long names or more than 255 entries in a group are dropped rather than truncated: the wire has a
    /// single byte for each, and a mangled name would silently configure the wrong content.
    /// </summary>
    public static byte[] Serialize(IEnumerable<WorldContentGroup> groups)
    {
        var ordered = (groups ?? [])
            .Where(group => group != null && group.Names.Count > 0)
            .OrderBy(group => group.CategoryId)
            .ToList();

        using var body = new MemoryStream();
        WriteUInt16(body, (ushort)Math.Min(ordered.Count, ushort.MaxValue));

        foreach (var group in ordered)
        {
            var category = Encoding.UTF8.GetBytes(group.CategoryName ?? string.Empty);
            if (category.Length is 0 or > byte.MaxValue)
                continue;

            body.WriteByte((byte)category.Length);
            body.Write(category);

            var names = new List<byte[]>();
            foreach (var name in group.Names)
            {
                var bytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
                if (bytes.Length is 0 or > byte.MaxValue)
                    continue;

                names.Add(bytes);
                if (names.Count == ushort.MaxValue)
                    break;
            }

            WriteUInt16(body, (ushort)names.Count);
            foreach (var name in names)
            {
                body.WriteByte((byte)name.Length);
                body.Write(name);
            }
        }

        return body.ToArray();
    }

    private static void WriteUInt16(MemoryStream stream, ushort value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)(value >> 8));
    }
}

/// <summary>One category of the pack: the client's category id and name, and the entries configured for it.</summary>
public class WorldContentGroup
{
    public byte CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public List<string> Names { get; } = [];
}
