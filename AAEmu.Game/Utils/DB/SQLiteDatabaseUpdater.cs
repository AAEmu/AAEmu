using AAEmu.Commons.IO;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.Utils.DB;

public static class SQLiteDatabaseUpdater
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Scans SQL/patches/compact/ for .sql files and applies any that haven't been run yet.
    /// Tracks applied migrations in a _migrations table inside compact.sqlite3.
    /// </summary>
    public static bool Run()
    {
        var patchesFolder = FindPatchesFolder();
        if (string.IsNullOrEmpty(patchesFolder))
        {
            Logger.Debug("No SQL/patches/compact/ folder found, skipping SQLite migrations.");
            return true;
        }

        var patchFiles = Directory.GetFiles(patchesFolder, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (patchFiles.Count == 0)
        {
            Logger.Debug("No SQLite patch files found.");
            return true;
        }

        using var connection = SQLite.CreateWriteConnection();

        EnsureMigrationsTable(connection);

        var applied = GetAppliedMigrations(connection);
        var isFirstRun = applied.Count == 0 && !MigrationsTableHadRows(connection);
        var pending = new List<string>();

        foreach (var file in patchFiles)
        {
            var name = Path.GetFileName(file);
            if (!applied.Contains(name))
                pending.Add(file);
        }

        if (pending.Count == 0)
        {
            Logger.Debug("SQLite database is up to date.");
            return true;
        }

        // On first run (fresh _migrations table), mark all existing patches as already applied
        // so we don't re-run patches that were applied manually before this system existed.
        if (isFirstRun)
        {
            Logger.Info($"First run of SQLite migration tracker — marking {pending.Count} existing patch(es) as applied.");
            foreach (var file in pending)
                RecordMigration(connection, Path.GetFileName(file), true, "Initialized");
            return true;
        }

        Logger.Info($"Applying {pending.Count} SQLite patch(es)...");
        foreach (var file in pending)
        {
            var name = Path.GetFileName(file);
            var sql = File.ReadAllText(file);

            using var transaction = connection.BeginTransaction();
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
                transaction.Commit();

                RecordMigration(connection, name, true, string.Empty);
                Logger.Info($"  Applied: {name}");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                RecordMigration(connection, name, false, ex.Message);
                Logger.Error($"  Failed: {name} — {ex.Message}");
                return false;
            }
        }

        Logger.Info("All SQLite patches applied successfully.");
        return true;
    }

    private static void EnsureMigrationsTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS _migrations (
                script_name TEXT PRIMARY KEY NOT NULL,
                installed   INTEGER NOT NULL DEFAULT 0,
                install_date TEXT NOT NULL,
                last_error  TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static HashSet<string> GetAppliedMigrations(SqliteConnection connection)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT script_name FROM _migrations WHERE installed = 1";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>
    /// Check if the _migrations table has any rows at all (to distinguish first-run from empty).
    /// </summary>
    private static bool MigrationsTableHadRows(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM _migrations";
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        return count > 0;
    }

    private static void RecordMigration(SqliteConnection connection, string scriptName, bool success, string error)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO _migrations (script_name, installed, install_date, last_error)
            VALUES (@name, @installed, @date, @error);
            """;
        cmd.Parameters.AddWithValue("@name", scriptName);
        cmd.Parameters.AddWithValue("@installed", success ? 1 : 0);
        cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@error", error);
        cmd.ExecuteNonQuery();
    }

    private static string FindPatchesFolder()
    {
        var currentDir = FileManager.AppPath;
        while (currentDir.Split(Path.DirectorySeparatorChar).Length > 1)
        {
            var testDir = Path.Combine(currentDir, "SQL", "patches", "compact");
            if (Directory.Exists(testDir))
                return testDir;

            try
            {
                currentDir = Directory.GetParent(currentDir)?.FullName ?? string.Empty;
            }
            catch
            {
                break;
            }
        }

        return string.Empty;
    }
}
