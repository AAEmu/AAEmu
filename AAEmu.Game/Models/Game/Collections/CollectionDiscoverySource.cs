namespace AAEmu.Game.Models.Game.Collections;

/// <summary>
/// Which content event a discovery arrived from. The collection content keys its watch records by
/// these event kinds (obtaining, unpacking or equipping an item type), so the source decides which
/// of the character's records the discovery reports into.
/// </summary>
public enum CollectionDiscoverySource
{
    /// <summary>The item type entered the character's possession.</summary>
    Acquired,

    /// <summary>The item type was unpacked.</summary>
    Unpacked,

    /// <summary>The item type was placed in the character's equipment.</summary>
    Equipped,
}
