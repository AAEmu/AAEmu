using System.Text;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Builds the world-content pack the client parses on entering the world (<c>SCWorldContentPacket</c>).
/// The client reads it as a count of content categories, each with the names that are configured for it,
/// and stores the matches in the per-category filter its features consult.
/// </summary>
/// <remarks>
/// Layout, taken from the client's own reader: <c>u16</c> category count, then per category a <c>u8</c>
/// category id, a <c>u8</c> name length with the name, and a <c>u16</c> entry count, then per entry a
/// <c>u8</c> name length with the name. The category ids are the client's own numbering, not ours.
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

    /// <summary>The client's id for a category name, matched without case. Null when it has no such category.</summary>
    public static byte? CategoryIdOf(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return null;

        for (var index = 0; index < CategoryNames.Length; index++)
        {
            if (string.Equals(CategoryNames[index], categoryName.Trim(), StringComparison.OrdinalIgnoreCase))
                return (byte)index;
        }

        return null;
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

            body.WriteByte(group.CategoryId);
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
