using System.IO;
using Microsoft.Data.Sqlite;

namespace AAEmu.BillServer.Cash;

/// <summary>Reads item display names from client compact.sqlite3 localized_texts.</summary>
public sealed class CompactItemNameCatalog : IDisposable
{
    private readonly SqliteConnection? _connection;
    private readonly string _languageColumn;
    private readonly Dictionary<uint, string?> _cache = new();

    public CompactItemNameCatalog(string? compactPath, string languageColumn = "en_us")
    {
        _languageColumn = SanitizeLanguageColumn(languageColumn);
        var path = ResolveCompactPath(compactPath);
        if (path is null)
            return;

        CompactPath = path;
        _connection = new SqliteConnection($"Data Source=file:{path}; Mode=ReadOnly");
        _connection.Open();
    }

    public bool IsAvailable => _connection is not null;

    public string CompactPath { get; private set; } = "";

    public static bool NeedsResolvedName(string? name) =>
        string.IsNullOrWhiteSpace(name)
        || name.StartsWith("Premium #", StringComparison.OrdinalIgnoreCase);

    public string? GetItemName(uint itemId)
    {
        if (itemId == 0)
            return null;

        if (_cache.TryGetValue(itemId, out var cached))
            return cached;

        if (_connection is null)
        {
            _cache[itemId] = null;
            return null;
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {_languageColumn}
            FROM localized_texts
            WHERE tbl_name = 'items' AND tbl_column_name = 'name' AND idx = @id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@id", (long)itemId);
        var value = cmd.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            _cache[itemId] = null;
            return null;
        }

        _cache[itemId] = value;
        return value;
    }

    public string ResolveDisplayName(string? currentName, uint itemId)
    {
        if (!NeedsResolvedName(currentName) && !string.IsNullOrWhiteSpace(currentName))
            return currentName!;

        return GetItemName(itemId) ?? currentName ?? "";
    }

    public static string? ResolveCompactPath(string? configuredPath)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates.Add(configuredPath.Trim());

        candidates.Add(Environment.GetEnvironmentVariable("AAEMU_CLIENT_COMPACT"));

        var fromExe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "client", "game", "db", "compact.sqlite3"));
        candidates.Add(fromExe);

        var fromExeAlt = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "client", "game", "db", "compact.sqlite3"));
        candidates.Add(fromExeAlt);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    private static string SanitizeLanguageColumn(string languageColumn)
    {
        var col = languageColumn.Trim().ToLowerInvariant();
        return col switch
        {
            "en_us" or "zh_cn" or "zh_tw" or "ko" or "ja" or "ru" or "de" or "fr" or "th" or "ind" or "en_sg" or "pt" or "es" => col,
            _ => "en_us"
        };
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
