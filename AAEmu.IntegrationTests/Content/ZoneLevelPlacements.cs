using AAEmu.World.Core.Relay;
using Xunit;

namespace AAEmu.IntegrationTests.Content;

/// <summary>
/// Counts real level-pack placements through the production placement catalog, so a test can tell a
/// board the cell files place from one that only exists in the compact tables.
/// </summary>
/// <remarks>
/// The root comes from the same resolver the world uses
/// (<see cref="ZoneGameDataRootResolver.EnvVarName"/> or World config), never a machine-specific path.
/// A caller gates on <see cref="IsConfigured"/> first: with no root there is nothing to read, and
/// counting that as "no placements" would make a board look unplaceable when the level files were
/// simply never there.
/// </remarks>
public static class ZoneLevelPlacements
{
    public const string SkipMessage =
        "Set " + ZoneGameDataRootResolver.EnvVarName +
        " to the extracted game data root to run the level placement tests.";

    public static bool IsConfigured => ZoneGameDataRootResolver.TryGetRoot() is not null;

    public static int Count(IReadOnlyCollection<uint> templateIds)
    {
        if (templateIds == null || templateIds.Count == 0)
            return 0;

        var root = ZoneGameDataRootResolver.TryGetRoot();
        if (root is null)
            return 0;

        var worlds = Path.Combine(root, "worlds");
        if (!Directory.Exists(worlds))
            return 0;

        var total = 0;
        foreach (var world in Directory.EnumerateDirectories(worlds))
        {
            var name = Path.GetFileName(world);
            foreach (var templateId in templateIds)
            {
                total += ZoneDoodadPlacementCatalog.GetByTemplate(name, templateId).Count;
            }
        }

        return total;
    }
}
