using AAEmu.Commons.IO;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.Utils.DB;

/// <summary>
/// Defensive boot-time check that the `holdables` table in compact.sqlite3 has the
/// six attack-animation columns (anim_r1_id, anim_l1_id, anim_r2_id, anim_l2_id,
/// anim_r3_id, anim_l3_id) required by the Auto-Attack system.
///
/// The shipped ArcheAge compact.sqlite3 already contains these columns with real
/// data. This helper only matters if the user is running with a stripped/custom
/// SQLite that lacks them — in that case it adds them as INT NULL columns.
///
/// All ops are best-effort; failures are logged but never throw the server.
/// </summary>
public static class HoldablesSchemaCheck
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly string[] RequiredColumns =
    [
        "anim_r1_id", "anim_l1_id",
        "anim_r2_id", "anim_l2_id",
        "anim_r3_id", "anim_l3_id"
    ];

    public static void EnsureColumns(string directory = "Data", string sqlite = "compact.sqlite3")
    {
        var dbPath = Path.Combine(FileManager.AppPath, directory, sqlite);
        if (!File.Exists(dbPath))
        {
            Logger.Warn("HoldablesSchemaCheck: {0} not found — skipping.", dbPath);
            return;
        }

        try
        {
            // Phase 1: open ReadOnly to scan columns
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var roConn = new SqliteConnection($"Data Source=file:{dbPath}; Mode=ReadOnly"))
            {
                roConn.Open();
                using var pragma = roConn.CreateCommand();
                pragma.CommandText = "PRAGMA table_info(holdables);";
                using var rdr = pragma.ExecuteReader();
                while (rdr.Read())
                {
                    // column 1 is the name in PRAGMA table_info
                    existing.Add(rdr.GetString(1));
                }
            }

            // Identify missing animation columns
            var missing = new List<string>();
            foreach (var col in RequiredColumns)
            {
                if (!existing.Contains(col))
                    missing.Add(col);
            }

            if (missing.Count == 0)
            {
                Logger.Info("HoldablesSchemaCheck: all 6 anim columns present, no migration needed.");
                return;
            }

            Logger.Warn("HoldablesSchemaCheck: {0} missing column(s) — adding: {1}",
                missing.Count, string.Join(", ", missing));

            // Phase 2: reopen ReadWrite to ALTER TABLE
            using var rwConn = new SqliteConnection($"Data Source=file:{dbPath}; Mode=ReadWrite");
            rwConn.Open();
            foreach (var col in missing)
            {
                using var alter = rwConn.CreateCommand();
                alter.CommandText = $"ALTER TABLE holdables ADD COLUMN {col} INT DEFAULT 0;";
                try
                {
                    alter.ExecuteNonQuery();
                    Logger.Info("  + added column {0}", col);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "  ! failed to add {0}", col);
                }
            }

            Logger.Info("HoldablesSchemaCheck: migration complete. Anim IDs default to 0 — " +
                        "auto-attack will fall back to fist animations until real values are set.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HoldablesSchemaCheck failed — server will continue without migration");
        }
    }
}
