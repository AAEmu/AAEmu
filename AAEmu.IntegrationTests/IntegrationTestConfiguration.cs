using System.Text.RegularExpressions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models;
using Microsoft.Extensions.Configuration;

namespace AAEmu.IntegrationTests;

/// <summary>
/// Single entry point for the configuration and content the integration tests need.
/// </summary>
/// <remarks>
/// <para>
/// The base <c>Config.json</c> is a template whose connection fields still carry the
/// <c>%db_host%</c> / <c>%db_port%</c> / <c>%db_user%</c> / <c>%db_password%</c> wildcards. Those are
/// substituted per machine — the container scripts rewrite them with <c>sed</c>, and a local
/// developer edits their own copy. Without substitution the configuration binder tries to convert the
/// literal <c>"%db_port%"</c> to <see cref="ushort"/> and throws a generic binder error, which says
/// nothing about where the value should have come from.
/// </para>
/// <para>
/// This type therefore mirrors the product layering used by <c>AAEmu.Game</c>: a committed base
/// config, the <c>Configurations/</c> overlay, and a gitignored <c>Config.Local.json</c> overlay that
/// carries the machine-local values. It then refuses to bind while any wildcard survives, and names
/// the files the caller has to fix.
/// </para>
/// </remarks>
public static partial class IntegrationTestConfiguration
{
    public const string BaseConfigFileName = "Config.json";
    public const string LocalConfigFileName = "Config.Local.json";
    public const string LocalConfigExampleFileName = "Config.Local.json.example";
    public const string ConfigurationsDirectoryName = "Configurations";
    public const string CompactDatabaseFileName = "compact.sqlite3";

    private static readonly Lock s_gate = new();
    private static bool s_loaded;

    /// <summary>The content root the tests read configuration and <c>Data/</c> from.</summary>
    public static string ContentRoot => FileManager.AppPath;

    /// <summary>
    /// Loads and binds the configuration once per test run. Safe to call from every fixture.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (s_loaded)
            return;

        lock (s_gate)
        {
            if (s_loaded)
                return;

            Load(ContentRoot);
            s_loaded = true;
        }
    }

    /// <summary>
    /// Builds the configuration the way the product does, but refuses to continue while a
    /// <c>%wildcard%</c> is still present, so the failure names the file to fix instead of surfacing
    /// as a binder conversion error.
    /// </summary>
    public static IConfigurationRoot Build(string contentRoot)
    {
        var baseConfig = Path.Combine(contentRoot, BaseConfigFileName);
        if (!File.Exists(baseConfig))
        {
            throw new InvalidOperationException(
                $"Integration tests need '{BaseConfigFileName}' in '{contentRoot}' but it is missing. " +
                $"Copy '{BaseConfigFileName}' from the checked-in ExampleConfig.json, or create " +
                $"'{LocalConfigFileName}' from '{LocalConfigExampleFileName}' and supply the base file " +
                "alongside it. The integration test project is configured to fall back to the example, " +
                "so a missing file here means the project file copy rule did not run.");
        }

        var configFiles = new List<string> { baseConfig };

        var configurationsRoot = Path.Combine(contentRoot, ConfigurationsDirectoryName);
        if (Directory.Exists(configurationsRoot))
        {
            configFiles.AddRange(Directory.GetFiles(configurationsRoot, "*.json", SearchOption.AllDirectories).Order());
        }

        var builder = new ConfigurationBuilder();
        foreach (var file in configFiles)
            builder.AddJsonFile(file, optional: false);

        // Machine-local overlay, same name and role as the Login/World/Game entry points.
        builder.AddJsonFile(Path.Combine(contentRoot, LocalConfigFileName), optional: true);

        var configuration = builder.Build();
        RejectUnboundWildcards(configuration, configFiles, contentRoot);
        return configuration;
    }

    /// <summary>Loads the configuration, binds it, and points <see cref="MySQL"/> at it.</summary>
    public static void Load(string contentRoot)
    {
        var configuration = Build(contentRoot);
        configuration.Bind(AppConfiguration.Instance);
        MySQL.SetConfiguration(AppConfiguration.Instance.Connections.MySQLProvider);
    }

    /// <summary>
    /// Returns every configuration value that is still a <c>%wildcard%</c>, sorted and de-duplicated.
    /// </summary>
    public static List<string> FindUnboundWildcards(IConfiguration configuration)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in configuration.AsEnumerable())
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('%') < 0)
                continue;
            if (WildcardPattern().IsMatch(value))
                found.Add(key);
        }
        return [.. found];
    }

    /// <summary>
    /// Fails with an actionable message when the configuration still carries an unsubstituted
    /// wildcard. Binding such a config raises a binder error that does not mention the cause.
    /// </summary>
    public static void RejectUnboundWildcards(IConfiguration configuration, IEnumerable<string> configFiles, string contentRoot)
    {
        var unbound = FindUnboundWildcards(configuration);
        if (unbound.Count == 0)
            return;

        // Only the base and the local overlay are worth naming; the Configurations/ files shipped
        // with the product are context, not something the caller can fix.
        var actionable = configFiles
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}{ConfigurationsDirectoryName}{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Distinct(StringComparer.Ordinal);

        throw new InvalidOperationException(
            $"Integration test configuration still contains unsubstituted %wildcards% at: " +
            $"{string.Join(", ", unbound)}.{Environment.NewLine}" +
            $"Loaded from: {string.Join(", ", actionable)}{Environment.NewLine}" +
            $"Fix: create '{LocalConfigFileName}' in '{contentRoot}' from the checked-in " +
            $"'{LocalConfigExampleFileName}' and set the real values there, or edit " +
            $"'{BaseConfigFileName}' directly. '{LocalConfigFileName}' is gitignored, so machine " +
            "values and credentials stay out of the repository.");
    }

    /// <summary>
    /// Returns the compact database the content-reading tests need, or explains how to provide it.
    /// </summary>
    public static string RequireCompactDatabase() => RequireCompactDatabase(ContentRoot);

    /// <summary>
    /// Returns the compact database under <paramref name="contentRoot"/>, or explains how to
    /// provide it. A large local artifact is gitignored, so a fresh clone has none.
    /// </summary>
    public static string RequireCompactDatabase(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "Data", CompactDatabaseFileName);
        if (File.Exists(path))
            return path;

        throw new InvalidOperationException(
            $"Integration tests that read game content need '{CompactDatabaseFileName}' at '{path}', " +
            "which does not exist. It is a large local artifact and is gitignored on purpose. Copy it " +
            $"from the AAEmu.Game output Data folder (see AAEmu.IntegrationTests/readme.md).");
    }

    [GeneratedRegex(@"^%[A-Za-z0-9_]+%$", RegexOptions.CultureInvariant)]
    private static partial Regex WildcardPattern();
}
