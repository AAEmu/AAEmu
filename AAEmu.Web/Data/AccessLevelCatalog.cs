using System.Text.Json;
using AAEmu.Commons.IO;

namespace AAEmu.Web.Data;

/// <summary>
/// A distinct access level found in AccessLevels.json, with how much it unlocks.
/// </summary>
/// <param name="Level">The numeric access level stored in <c>accounts.access_level</c>.</param>
/// <param name="Label">A human-readable name for the tier.</param>
/// <param name="CommandsUnlocked">Commands that require exactly this level.</param>
/// <param name="CumulativeCommands">Commands usable at this level or below.</param>
public readonly record struct AccessLevelTier(int Level, string Label, int CommandsUnlocked, int CumulativeCommands);

public interface IAccessLevelCatalog
{
    /// <summary>Distinct access levels, ascending. Empty when the file could not be loaded.</summary>
    IReadOnlyList<AccessLevelTier> Tiers { get; }

    /// <summary>Total number of commands defined in AccessLevels.json.</summary>
    int TotalCommands { get; }

    /// <summary>How many commands an account at <paramref name="level"/> can use.</summary>
    int CommandsAvailableAt(int level);

    /// <summary>A label for an arbitrary level, including ones not present in the file.</summary>
    string DescribeLevel(int level);
}

/// <summary>
/// Reads the command-to-access-level map that AAEmu.Game ships in
/// <c>Configurations/AccessLevels.json</c> (linked into this project's output at build time).
/// </summary>
/// <remarks>
/// That file maps each chat command to the minimum access level required to run it — it is not a
/// list of named roles. The tiers here are therefore derived from the distinct values it contains,
/// with conventional names attached to the well-known ones. The file is JSON with <c>//</c>
/// comments, so comment skipping is enabled.
/// </remarks>
public class AccessLevelCatalog : IAccessLevelCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // Derived from the section comments in AccessLevels.json.
    private static readonly Dictionary<int, string> KnownLabels = new()
    {
        [0] = "Everyone",
        [50] = "Moderator",
        [100] = "Admin",
        [999] = "Disabled"
    };

    private readonly Dictionary<string, int> _commands;

    public IReadOnlyList<AccessLevelTier> Tiers { get; }

    public int TotalCommands => _commands.Count;

    public AccessLevelCatalog(ILogger<AccessLevelCatalog> logger)
    {
        _commands = Load(logger);

        var cumulative = 0;
        Tiers = _commands.Values
            .Distinct()
            .Order()
            .Select(level =>
            {
                var unlocked = _commands.Values.Count(l => l == level);
                cumulative += unlocked;
                return new AccessLevelTier(level, DescribeLevel(level), unlocked, cumulative);
            })
            .ToList();
    }

    public int CommandsAvailableAt(int level) => _commands.Values.Count(required => required <= level);

    public string DescribeLevel(int level) =>
        KnownLabels.TryGetValue(level, out var label) ? label : $"Level {level}";

    private static Dictionary<string, int> Load(ILogger logger)
    {
        var path = Path.Combine(FileManager.AppPath, "AccessLevels.json");

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonOptions.ReadCommentHandling,
                AllowTrailingCommas = JsonOptions.AllowTrailingCommas
            });

            if (!document.RootElement.TryGetProperty("AccessLevel", out var accessLevel)
                || accessLevel.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning("{Path} has no AccessLevel object; access level hints are unavailable.", path);
                return [];
            }

            var commands = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in accessLevel.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var level))
                    commands[property.Name] = level;
            }

            logger.LogInformation("Loaded {Count} command access levels from {Path}.", commands.Count, path);
            return commands;
        }
        catch (Exception e)
        {
            // Not fatal: the account page simply shows the raw number without hints.
            logger.LogWarning(e, "Could not load {Path}; access level hints are unavailable.", path);
            return [];
        }
    }
}
