namespace AAEmu.Game.Models.Game;

/// <summary>
/// Language cells on <c>localized_texts</c> are every column except the row key.
/// Search uses those cells as they are — empty values are skipped, nothing is invented.
/// </summary>
public static class LocalizedTextSearchRules
{
    public static bool IsKeyColumn(string column) =>
        column is "id" or "tbl_name" or "tbl_column_name" or "idx";

    public static List<string> LanguageColumns(IEnumerable<string> tableColumns)
    {
        var columns = new List<string>();
        if (tableColumns == null)
            return columns;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in tableColumns)
        {
            if (string.IsNullOrWhiteSpace(column) || IsKeyColumn(column))
                continue;
            if (seen.Add(column))
                columns.Add(column);
        }

        return columns;
    }

    public static List<string> UniqueNames(IEnumerable<string> values)
    {
        var names = new List<string>();
        if (values == null)
            return names;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (seen.Add(value))
                names.Add(value);
        }

        return names;
    }

    public static string BuildSearchString(string templateName, IEnumerable<string> localizedNames)
    {
        var names = UniqueNames([templateName]);
        names.AddRange(UniqueNames(localizedNames));
        return string.Join(" ", UniqueNames(names)).ToLower();
    }
}
