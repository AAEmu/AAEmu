using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Whether a <c>slave_initial_items</c> row produced something that can go on the hull.
/// </summary>
/// <remarks>
/// Compact still names item 43000 on rows 203, 238 and 247 (hulls 620, 624, 628 and 633), and that
/// template does not exist. <c>ItemManager.Create</c> then returns null; treating that as a created
/// item throws on <c>Id</c> and aborts the rest of the seed.
/// </remarks>
public static class SlaveStarterSeedRules
{
    /// <summary>Whether the created item can be added, or must be skipped without a <c>ReleaseId</c>.</summary>
    public static bool HasCreatedItem(Item item) => item != null;
}
