namespace AAEmu.Game.Models.Game.Housing;

/// <summary>One material a rebuild target costs (<c>housing_rebuilding_materials</c>).</summary>
public readonly record struct HousingRebuildMaterial(uint ItemId, int Count);

/// <summary>
/// One rebuild target — a building a house can be changed into (<c>housing_rebuildings</c>).
/// </summary>
/// <remarks>
/// The target is picked in the client's rebuild window and started with the skill the row carries
/// (<see cref="SkillId"/>): the client casts that skill at the house, and the cast is what the server acts
/// on. <see cref="HousingId"/> is the housing template the house becomes.
/// </remarks>
public sealed class HousingRebuildTarget
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public uint SkillId { get; init; }
    public uint HousingId { get; init; }
    public int LaborPower { get; init; }
    public List<HousingRebuildMaterial> Materials { get; init; } = [];
}

/// <summary>
/// One rebuild pack — the set of targets one house may be rebuilt into (<c>housing_rebuilding_packs</c>,
/// linked through <c>housing_rebuilding_pack_rebuildings</c>). A house names its pack in
/// <c>housings.housing_rebuilding_pack_id</c>.
/// </summary>
public sealed class HousingRebuildPack
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>The targets this pack offers, in the position the table gives them.</summary>
    public List<uint> TargetIds { get; init; } = [];
}

/// <summary>Why a rebuild was refused, or <see cref="None"/> when it may go ahead.</summary>
public enum HousingRebuildRefusal
{
    None = 0,

    /// <summary>The character does not own the house being rebuilt.</summary>
    NotOwner,

    /// <summary>The house's pack does not offer this target.</summary>
    TargetNotInPack,

    /// <summary>A material the target costs is not in the bag in the required amount.</summary>
    MissingMaterials,

    /// <summary>The character has less labor than the target spends.</summary>
    NotEnoughLabor
}

/// <summary>
/// The decision a rebuild makes before it touches anything: whether a house's pack offers a target, and
/// whether this character may pay for it. Pure, so the table-driven answers can be tested without a house.
/// </summary>
public static class HousingRebuildRules
{
    /// <summary>Whether <paramref name="pack"/> offers <paramref name="targetId"/> at all.</summary>
    public static bool IsOfferedByPack(HousingRebuildPack pack, uint targetId) =>
        pack?.TargetIds.Contains(targetId) == true;

    /// <summary>
    /// The first reason this rebuild cannot go ahead, checked in the order a player would hit them: not the
    /// owner, a target the house's pack does not offer, materials missing from the bag, then labor.
    /// </summary>
    /// <param name="isOwner">Whether the character owns the house.</param>
    /// <param name="pack">The house's rebuild pack, or null when it has none.</param>
    /// <param name="target">The target being rebuilt into, or null when the skill named none.</param>
    /// <param name="bagCounts">How many of each item the character carries, by item template id.</param>
    /// <param name="laborPower">The character's labor.</param>
    public static HousingRebuildRefusal Check(
        bool isOwner,
        HousingRebuildPack pack,
        HousingRebuildTarget target,
        IReadOnlyDictionary<uint, int> bagCounts,
        int laborPower)
    {
        if (!isOwner)
            return HousingRebuildRefusal.NotOwner;

        if (target == null || !IsOfferedByPack(pack, target.Id))
            return HousingRebuildRefusal.TargetNotInPack;

        foreach (var material in target.Materials)
        {
            var held = bagCounts != null && bagCounts.TryGetValue(material.ItemId, out var count) ? count : 0;
            if (held < material.Count)
                return HousingRebuildRefusal.MissingMaterials;
        }

        return laborPower < target.LaborPower
            ? HousingRebuildRefusal.NotEnoughLabor
            : HousingRebuildRefusal.None;
    }
}
