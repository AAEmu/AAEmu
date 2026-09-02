#nullable enable
using AAEmu.Commons.IO;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.Utils.DB;

public sealed class CompactSqliteUpdateResult
{
    public int ScriptsApplied { get; set; }
    public int ScriptsAlreadyInstalled { get; set; }
    public int TablesSkippedMissing { get; set; }
    public int StatementsExecuted { get; set; }
    public List<string> Errors { get; } = [];
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Applies <c>SQL/compact/*_compact_*.sql</c> onto compact.sqlite3 (or game.sqlite3).
/// This is not the MySQL <c>SQL/updates</c> runner: compact files are gitignored, so
/// content deltas have to ship as SQL. Pending scripts always run; the first boot does
/// not assume they are already installed.
/// </summary>
public static class CompactSqliteUpdater
{
    public const string TrackingTable = "_aaemu_compact_updates";
    public const string SkipEnvVar = "AAEMU_SKIP_COMPACT_UPDATES";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static CompactSqliteUpdateResult ApplyDefault()
    {
        var compactPath = Path.Combine(FileManager.AppPath, "Data", "compact.sqlite3");
        return ApplyAt(compactPath, FileManager.AppPath, AppContext.BaseDirectory);
    }

    /// <summary>
    /// Apply pending compact scripts. <paramref name="searchRoots"/> are walked for
    /// <c>SQL/compact/*_compact_*.sql</c> (Game content root first, then the process
    /// directory so World still finds scripts copied next to the executable).
    /// </summary>
    public static CompactSqliteUpdateResult ApplyAt(string compactPath, params string[] searchRoots)
    {
        var result = new CompactSqliteUpdateResult();
        if (IsSkipped())
        {
            Logger.Info("CompactSqliteUpdater: skipped ({0}=1)", SkipEnvVar);
            return result;
        }

        if (!File.Exists(compactPath))
        {
            Logger.Warn("CompactSqliteUpdater: {0} not found — skipping", compactPath);
            return result;
        }

        var scriptsDir = FindScriptsDirectory(searchRoots);
        if (string.IsNullOrEmpty(scriptsDir))
        {
            const string message =
                "SQL/compact *_compact_*.sql not found. Copy SQL/compact next to the Game content root or the World executable.";
            result.Errors.Add(message);
            Logger.Error("CompactSqliteUpdater: {0}", message);
            return result;
        }

        return Apply(compactPath, scriptsDir);
    }

    public static bool IsSkipped()
    {
        var value = Environment.GetEnvironmentVariable(SkipEnvVar);
        return value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static CompactSqliteUpdateResult Apply(string compactDbPath, string scriptsDirectory)
    {
        var result = new CompactSqliteUpdateResult();
        if (!File.Exists(compactDbPath))
        {
            Logger.Warn("CompactSqliteUpdater: {0} not found — skipping", compactDbPath);
            return result;
        }

        if (!Directory.Exists(scriptsDirectory))
        {
            result.Errors.Add($"scripts directory not found: {scriptsDirectory}");
            return result;
        }

        var scripts = Directory.GetFiles(scriptsDirectory, "*_compact_*.sql", SearchOption.TopDirectoryOnly);
        Array.Sort(scripts, StringComparer.OrdinalIgnoreCase);
        if (scripts.Length == 0)
        {
            result.Errors.Add($"no *_compact_*.sql files in {scriptsDirectory}");
            return result;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source=file:{compactDbPath}; Mode=ReadWrite");
            connection.DefaultTimeout = 120;
            connection.Open();
            EnsureTrackingTable(connection);
            var installed = LoadInstalled(connection);

            foreach (var scriptPath in scripts)
            {
                var name = Path.GetFileName(scriptPath);
                if (installed.Contains(name))
                {
                    result.ScriptsAlreadyInstalled++;
                    continue;
                }

                try
                {
                    var text = File.ReadAllText(scriptPath);
                    using var transaction = connection.BeginTransaction();
                    ApplyScript(connection, text, result, transaction);
                    Record(connection, name, installed: true, error: "", transaction);
                    transaction.Commit();
                    result.ScriptsApplied++;
                    Logger.Info("CompactSqliteUpdater: installed {0}", name);
                }
                catch (Exception ex)
                {
                    Record(connection, name, installed: false, error: ex.Message);
                    result.Errors.Add($"{name}: {ex.Message}");
                    Logger.Error(ex, "CompactSqliteUpdater: failed {0}", name);
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            Logger.Error(ex, "CompactSqliteUpdater failed");
        }

        return result;
    }

    public static string? FindScriptsDirectory(params string?[] startDirectories)
    {
        foreach (var start in DistinctStarts(startDirectories))
        {
            var current = start;
            while (!string.IsNullOrEmpty(current))
            {
                var candidate = Path.Combine(current, "SQL", "compact");
                if (Directory.Exists(candidate) &&
                    Directory.GetFiles(candidate, "*_compact_*.sql", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    return candidate;
                }

                var parent = Directory.GetParent(current);
                if (parent == null)
                    break;
                current = parent.FullName;
            }
        }

        return null;
    }

    internal static void ApplyScript(
        SqliteConnection connection,
        string script,
        CompactSqliteUpdateResult result,
        SqliteTransaction transaction)
    {
        var statements = CompactSqlScript.Parse(script);
        foreach (var statement in statements)
        {
            if (!string.IsNullOrEmpty(statement.Table) && !TableExists(connection, statement.Table, transaction))
            {
                result.TablesSkippedMissing++;
                continue;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement.Sql;
            command.ExecuteNonQuery();
            result.StatementsExecuted++;
        }
    }

    private static IEnumerable<string> DistinctStarts(string?[]? startDirectories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (startDirectories != null)
        {
            foreach (var start in startDirectories)
            {
                if (string.IsNullOrWhiteSpace(start))
                    continue;
                var full = Path.GetFullPath(start);
                if (seen.Add(full))
                    yield return full;
            }
        }

        if (seen.Count == 0 && !string.IsNullOrWhiteSpace(FileManager.AppPath))
            yield return Path.GetFullPath(FileManager.AppPath);
    }

    private static bool TableExists(SqliteConnection connection, string table, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() != null;
    }

    private static void EnsureTrackingTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE TABLE IF NOT EXISTS {TrackingTable} (" +
            "script_name TEXT NOT NULL PRIMARY KEY, " +
            "installed INTEGER NOT NULL, " +
            "install_date TEXT NOT NULL, " +
            "last_error TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    private static HashSet<string> LoadInstalled(SqliteConnection connection)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT script_name FROM {TrackingTable} WHERE installed = 1";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    private static void Record(
        SqliteConnection connection,
        string scriptName,
        bool installed,
        string error,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {TrackingTable} (script_name, installed, install_date, last_error) " +
            "VALUES ($name, $installed, $date, $error) " +
            "ON CONFLICT(script_name) DO UPDATE SET " +
            "installed = excluded.installed, install_date = excluded.install_date, last_error = excluded.last_error";
        command.Parameters.AddWithValue("$name", scriptName);
        command.Parameters.AddWithValue("$installed", installed ? 1 : 0);
        command.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$error", error ?? "");
        command.ExecuteNonQuery();
    }
}
