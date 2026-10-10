using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// The time of day a world's level authors, from
/// <c>worlds/{name}/time_of_day.xml</c> under <see cref="ZoneGameDataRootResolver"/>.
/// </summary>
/// <remarks>
/// The client lights a world from this file; an instance dedicate is seeded with it so the
/// instance runs on the hour its own level authored instead of a value baked into the server.
/// A world that authors no time (or an unreadable/out-of-range one) is reported and seeded with
/// nothing, which leaves the dedicate on its own default — the file has to say it, not the code.
/// </remarks>
public static partial class ZoneTimeOfDayCatalog
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly ConcurrentDictionary<string, AuthoredTimeOfDay?> ByWorld =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<(string World, uint ZoneId), AuthoredTimeOfDay?> ByZone = new();

    /// <summary>A world's authored clock: the start hour in [0,24) and the level's animation speed.</summary>
    public readonly record struct AuthoredTimeOfDay(float StartHour, float AnimSpeed);

    [GeneratedRegex("""<TimeOfDay\b[^>]*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeOfDayElement();

    [GeneratedRegex("\\bTime\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartHourAttribute();

    [GeneratedRegex("\\bTimeAnimSpeed\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnimSpeedAttribute();

    /// <summary>The authored clock of a world, or null when the level does not author one.</summary>
    public static AuthoredTimeOfDay? TryGet(string worldName)
    {
        if (string.IsNullOrWhiteSpace(worldName))
            return null;

        return ByWorld.GetOrAdd(worldName.Trim(), Load);
    }

    /// <summary>Drop the cached entry for a world (or every world) so the next read re-parses.</summary>
    public static void Invalidate(string worldName = null)
    {
        if (string.IsNullOrWhiteSpace(worldName))
        {
            ByWorld.Clear();
            ByZone.Clear();
            return;
        }

        ByWorld.TryRemove(worldName.Trim(), out _);
        foreach (var key in ByZone.Keys.Where(k => k.World.Equals(worldName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList())
            ByZone.TryRemove(key, out _);
    }

    /// <summary>
    /// The authored clock of one zone of a world, or null when that zone's level authors none.
    /// </summary>
    /// <remarks>
    /// An instance level authors its clock per zone — <c>worlds/{name}/zone/{zoneId}/time_of_day.xml</c> —
    /// and that file is the one carrying the level's own static hour. The world-level file is the editor's
    /// *saved* clock, which for a level saved at the end of its day seeds the map's dawn instead of the
    /// night it runs on, so the zone's own file is asked for first.
    /// </remarks>
    public static AuthoredTimeOfDay? TryGetZone(string worldName, uint zoneId)
    {
        if (string.IsNullOrWhiteSpace(worldName) || zoneId == 0)
            return null;

        return ByZone.GetOrAdd((worldName.Trim(), zoneId), key => LoadZone(key.World, key.ZoneId));
    }

    private static AuthoredTimeOfDay? LoadZone(string worldName, uint zoneId)
    {
        var root = ZoneGameDataRootResolver.TryGetRoot();
        if (root is null)
            return null;

        var path = Path.Combine(root, "worlds", worldName, "zone", zoneId.ToString(), "time_of_day.xml");
        if (!File.Exists(path))
            return null;

        try
        {
            var authored = Parse(File.ReadAllText(path));
            if (authored is not null)
                Logger.Debug("Authored zone time of day for {0}/{1}: start={2:F4}h speed={3}",
                    worldName, zoneId, authored.Value.StartHour, authored.Value.AnimSpeed);
            return authored;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read zone time_of_day.xml for {0}/{1} at {2}", worldName, zoneId, path);
            return null;
        }
    }

    /// <summary>Replace the cached value for a world (unit tests only).</summary>
    public static void SeedForTests(string worldName, AuthoredTimeOfDay? value) =>
        ByWorld[worldName] = value;

    private static AuthoredTimeOfDay? Load(string worldName)
    {
        var root = ZoneGameDataRootResolver.TryGetRoot();
        if (root is null)
            return null;

        var path = Path.Combine(root, "worlds", worldName, "time_of_day.xml");
        if (!File.Exists(path))
        {
            Logger.Error(
                "time_of_day.xml missing for world {0} at {1}; the instance is not seeded with an " +
                "authored hour because the level does not author one",
                worldName, path);
            return null;
        }

        try
        {
            var authored = Parse(File.ReadAllText(path));
            if (authored is null)
                Logger.Error("time_of_day.xml for world {0} has no usable <TimeOfDay Time=...> ({1})",
                    worldName, path);
            else
                Logger.Debug("Authored time of day for world {0}: start={1:F4}h speed={2}",
                    worldName, authored.Value.StartHour, authored.Value.AnimSpeed);
            return authored;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read time_of_day.xml for world {0} at {1}", worldName, path);
            return null;
        }
    }

    /// <summary>
    /// The authored clock in a <c>time_of_day.xml</c> document, or null when the element is absent,
    /// its hour is missing, or the hour is outside [0,24). Pure, so the format is pinned by a test.
    /// </summary>
    public static AuthoredTimeOfDay? Parse(string timeOfDayXml)
    {
        if (string.IsNullOrWhiteSpace(timeOfDayXml))
            return null;

        var element = TimeOfDayElement().Match(timeOfDayXml);
        if (!element.Success)
            return null;

        var hourAttribute = StartHourAttribute().Match(element.Value);
        if (!hourAttribute.Success ||
            !float.TryParse(hourAttribute.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var hour))
            return null;

        // Level XML stores the hour in [0,24). A value outside it is not a time this server can
        // seed, so it is refused rather than wrapped into a different part of the day.
        if (hour < 0f || hour >= 24f)
            return null;

        var speedAttribute = AnimSpeedAttribute().Match(element.Value);
        var speed = 0f;
        if (speedAttribute.Success)
            _ = float.TryParse(speedAttribute.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out speed);

        return new AuthoredTimeOfDay(hour, speed);
    }
}
