using System.Text.RegularExpressions;
using System.Text;

using AAEmu.Commons.IO;
using AAEmu.Commons.Models;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Commons.Utils.Updater;

/// <summary>
/// Creates a configured MySQL schema when it is missing and imports its base SQL dump once.
/// </summary>
public static partial class MySqlDatabaseBootstrap
{
    private const string CharactersTable = "characters";
    private const string BootstrapMarkerTable = "aaemu_bootstrap_import";
    private const string CompletionSentinelTable = "character_achievements";
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Replaces the schema directives in a base dump with the configured schema name.
    /// </summary>
    public static string RewriteBaseSchemaSql(string sql, string targetDatabase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabase);
        ValidateSchemaName(targetDatabase);

        var rewritten = CreateDatabaseRegex().Replace(sql, $"CREATE DATABASE IF NOT EXISTS `{targetDatabase}`;");
        return UseDatabaseRegex().Replace(rewritten, $"USE `{targetDatabase}`;");
    }

    /// <summary>
    /// Prepares a base dump for importing into an already-created schema.
    /// </summary>
    public static string PrepareImportSql(string sql, string targetDatabase)
    {
        var rewritten = RewriteBaseSchemaSql(sql, targetDatabase);
        return CreateDatabaseRegex().Replace(rewritten, string.Empty);
    }

    /// <summary>
    /// Determines whether a base schema import is required for the schema state observed at startup.
    /// </summary>
    public static bool ShouldImportBaseSchema(bool hasCharactersTable, bool hasBootstrapMarker)
    {
        return !hasCharactersTable || hasBootstrapMarker;
    }

    /// <summary>
    /// Ensures the configured schema exists and the base dump was imported.
    /// </summary>
    public static bool EnsureDatabase(MySqlConnectionSettings settings, string baseSchemaFileName)
    {
        var database = settings?.Database ?? string.Empty;

        try
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentException.ThrowIfNullOrWhiteSpace(settings.Database);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseSchemaFileName);
            ValidateSchemaName(settings.Database);
            database = settings.Database;

            using (var serverConnection = OpenConnection(settings, string.Empty))
            {
                if (!SchemaExists(serverConnection, settings.Database))
                {
                    Logger.Info("MySQL schema `{0}` is missing; creating it", settings.Database);
                    CreateSchema(serverConnection, settings.Database);
                }
            }

            using var schemaConnection = OpenConnection(settings, settings.Database);
            var hasCharactersTable = TableExists(schemaConnection, settings.Database, CharactersTable);
            var hasBootstrapMarker = TableExists(schemaConnection, settings.Database, BootstrapMarkerTable);
            if (!ShouldImportBaseSchema(hasCharactersTable, hasBootstrapMarker))
                return true;

            var schemaPath = FindBaseSchemaFile(baseSchemaFileName);
            if (string.IsNullOrWhiteSpace(schemaPath))
            {
                Logger.Fatal("Base schema file `{0}` was not found under an SQL folder", baseSchemaFileName);
                return false;
            }

            var statements = SplitBaseSchemaStatements(
                PrepareImportSql(File.ReadAllText(schemaPath), settings.Database));
            CreateBootstrapMarker(schemaConnection);
            Logger.Warn("MySQL schema `{0}` requires base import; importing `{1}`", settings.Database, schemaPath);
            var executed = ExecuteStatements(schemaConnection, statements);
            if (executed <= 0 || !TableExists(schemaConnection, settings.Database, CompletionSentinelTable))
            {
                Logger.Fatal("Base schema import did not create the completion table `{0}` in `{1}`",
                    CompletionSentinelTable, settings.Database);
                return false;
            }

            DropBootstrapMarker(schemaConnection);
            Logger.Info("Imported base schema into `{0}` ({1} statements)", settings.Database, executed);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "Failed to ensure MySQL schema `{0}`", database);
            return false;
        }
    }

    public static string FindBaseSchemaFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var found = FindBaseSchemaFileAbove(FileManager.AppPath, fileName);
        if (!string.IsNullOrWhiteSpace(found))
            return found;

        return FindBaseSchemaFileAbove(Environment.CurrentDirectory, fileName);
    }

    public static List<string> SplitBaseSchemaStatements(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var statements = new List<string>();
        var statement = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inBacktick = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                    statement.Append(current);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBacktick)
            {
                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    index++;
                    continue;
                }

                if (current == '-' && next == '-' && index + 2 < sql.Length && char.IsWhiteSpace(sql[index + 2]))
                {
                    inLineComment = true;
                    index++;
                    continue;
                }

                if (current == ';')
                {
                    AddStatement(statements, statement);
                    continue;
                }
            }

            statement.Append(current);

            if (current == '`' && !inSingleQuote && !inDoubleQuote)
            {
                inBacktick = !inBacktick;
                continue;
            }

            if (current == '\'' && !inDoubleQuote && !inBacktick && !IsEscaped(sql, index))
            {
                if (inSingleQuote && next == '\'')
                {
                    statement.Append(next);
                    index++;
                }
                else
                {
                    inSingleQuote = !inSingleQuote;
                }

                continue;
            }

            if (current == '"' && !inSingleQuote && !inBacktick && !IsEscaped(sql, index))
                inDoubleQuote = !inDoubleQuote;
        }

        if (inSingleQuote || inDoubleQuote || inBacktick || inBlockComment)
            throw new InvalidOperationException("Base SQL dump contains an unterminated literal or comment.");

        AddStatement(statements, statement);
        return statements;
    }

    private static int ExecuteStatements(MySqlConnection connection, List<string> statements)
    {
        var executed = 0;
        for (var index = 0; index < statements.Count; index++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statements[index];
            try
            {
                command.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                Logger.Fatal(ex, "Base schema statement {0} of {1} failed: {2}", index + 1, statements.Count,
                    statements[index]);
                throw;
            }
            executed++;
        }

        return executed;
    }

    private static void AddStatement(List<string> statements, StringBuilder statement)
    {
        var sql = statement.ToString().Trim();
        statement.Clear();
        if (!string.IsNullOrWhiteSpace(sql))
            statements.Add(sql);
    }

    private static bool IsEscaped(string sql, int index)
    {
        var slashCount = 0;
        for (var current = index - 1; current >= 0 && sql[current] == '\\'; current--)
            slashCount++;

        return (slashCount & 1) != 0;
    }

    private static string FindBaseSchemaFileAbove(string startPath, string fileName)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "SQL", fileName);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return string.Empty;
    }

    private static void ValidateSchemaName(string database)
    {
        if (database.IndexOf('`') >= 0 || database.IndexOf('\0') >= 0)
            throw new ArgumentException($"Invalid MySQL schema name: `{database}`", nameof(database));
    }

    private static bool SchemaExists(MySqlConnection connection, string database)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`SCHEMATA` WHERE `SCHEMA_NAME` = @schema_name;";
        command.Parameters.AddWithValue("@schema_name", database);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool TableExists(MySqlConnection connection, string database, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`TABLES` WHERE `TABLE_SCHEMA` = @schema_name AND `TABLE_NAME` = @table_name;";
        command.Parameters.AddWithValue("@schema_name", database);
        command.Parameters.AddWithValue("@table_name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void CreateSchema(MySqlConnection connection, string database)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;";
        command.ExecuteNonQuery();
    }

    private static void CreateBootstrapMarker(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE IF NOT EXISTS `{BootstrapMarkerTable}` (`id` tinyint NOT NULL PRIMARY KEY) ENGINE=InnoDB;";
        command.ExecuteNonQuery();
    }

    private static void DropBootstrapMarker(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{BootstrapMarkerTable}`;";
        command.ExecuteNonQuery();
    }

    private static MySqlConnection OpenConnection(MySqlConnectionSettings settings, string database)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Host ?? "localhost",
            Port = settings.Port == 0 ? (uint)3306 : settings.Port,
            UserID = settings.User ?? "root",
            Password = settings.Password ?? string.Empty,
            Database = database,
            Pooling = false,
            CharacterSet = "utf8mb4",
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true,
            DefaultCommandTimeout = 600,
            SslMode = settings.SslMode,
            AllowPublicKeyRetrieval = true
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    [GeneratedRegex(@"CREATE\s+DATABASE\s+IF\s+NOT\s+EXISTS\s+`?[^`;\s]+`?\s*;?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateDatabaseRegex();

    [GeneratedRegex(@"(?m)^\s*USE[ \t]+`?[^`;\s]+`?\s*;?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UseDatabaseRegex();
}
