using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>One material a rebuild target costs (<c>housing_rebuilding_materials</c>).</summary>
public readonly record struct HousingRebuildMaterial(uint ItemId, int Count);

/// <summary>
/// One rebuild target — a building a house can be changed into (<c>housing_rebuildings</c>).
/// </summary>
/// <remarks>
/// The rebuild window casts the skill the row carries at the house. Several targets share that skill, so
/// the cast also names <see cref="HousingId"/> — the housing template the house becomes.
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

/// <summary>How a rebuilt house lands, and which skills the owner needs to carry it out.</summary>
public static class HousingRebuildLanding
{
    /// <summary>The id a house request sends when it means "the house this character is working on".</summary>
    public const ushort NoHouseId = 0xFFFF;

    /// <summary>
    /// Whether a request's own id names a house. A window that opens from the house already on screen sends
    /// <see cref="NoHouseId"/> rather than repeating the id, and that is a house, not a missing one.
    /// </summary>
    public static bool NamesAHouse(ushort tlId) => tlId is > 0 and not NoHouseId;

    /// <summary>
    /// The build step a house sits on after it is rebuilt into a target with
    /// <paramref name="buildStepCount"/> steps: the completion stage (step 0) when the target has steps,
    /// finished (-1) when it has none. A remodel is not finished work — the client's own window says the
    /// building "must go through the Completion stage" — and the completion is the step's own skill cast.
    /// </summary>
    public static int LandingStep(int buildStepCount) => buildStepCount > 0 ? 0 : -1;

    /// <summary>
    /// The skills a house's pack requires of its owner: one start skill per target plus the completion
    /// skill of every build step the target is built through. Returned in a stable order, without
    /// duplicates and without zeroes.
    /// </summary>
    public static IReadOnlyList<uint> RequiredSkills(IEnumerable<uint> targetSkillIds,
        IEnumerable<uint> completionSkillIds)
    {
        var skills = new List<uint>();
        foreach (var skillId in targetSkillIds.Concat(completionSkillIds))
        {
            if (skillId != 0 && !skills.Contains(skillId))
                skills.Add(skillId);
        }

        return skills;
    }

    /// <summary>Those of <paramref name="required"/> the character does not know yet.</summary>
    public static IReadOnlyList<uint> MissingSkills(IEnumerable<uint> required, Func<uint, bool> knows) =>
        required.Where(skillId => !knows(skillId)).ToList();
}

/// <summary>
/// How far a house extends for skill range. The plot's garden radius is the building's size;
/// measuring only to the house origin treats the player as standing in empty space.
/// </summary>
public static class HousingDistanceRules
{
    public static float OccupiedRadius(float gardenRadius, float scale) =>
        Math.Max(0f, gardenRadius) * Math.Max(0f, scale);
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
    /// The pack row Confirm asked for: same start skill as the other designs, plus the housing
    /// template the extra named. A skill-only match is not enough — one skill starts every target
    /// in a pack.
    /// </summary>
    public static HousingRebuildTarget PickTarget(
        HousingRebuildPack pack,
        IEnumerable<HousingRebuildTarget> targets,
        uint skillId,
        uint housingId)
    {
        if (pack == null || targets == null || skillId == 0 || housingId == 0)
            return null;

        foreach (var target in targets)
        {
            if (target != null
                && target.SkillId == skillId
                && target.HousingId == housingId
                && IsOfferedByPack(pack, target.Id))
                return target;
        }

        return null;
    }

    /// <summary>
    /// The skills that start a rebuild, by skill id, built from the targets packs actually offer.
    /// </summary>
    /// <remarks>
    /// A target no pack names can never be picked, and the shipped tables carry rows like that: six targets
    /// sit in no pack at all, and one of them is a test row claiming skill 2 — the basic melee attack — with
    /// 696 materials. Indexing those would make an ordinary swing look like a remodel cast, so the index is
    /// built from the packs rather than from <c>housing_rebuildings</c> alone. Several targets still share a
    /// skill; the entry is only a "this is a remodel skill" test, and the row itself is named by the cast.
    /// </remarks>
    public static IReadOnlyDictionary<uint, uint> BuildSkillIndex(
        IEnumerable<HousingRebuildTarget> targets,
        IEnumerable<HousingRebuildPack> packs)
    {
        var byId = new Dictionary<uint, HousingRebuildTarget>();
        foreach (var target in targets ?? [])
        {
            if (target != null)
                byId[target.Id] = target;
        }

        var index = new Dictionary<uint, uint>();
        foreach (var pack in packs ?? [])
        {
            foreach (var targetId in pack?.TargetIds ?? [])
            {
                if (targetId != 0 && byId.TryGetValue(targetId, out var target) && target.SkillId != 0)
                    index[target.SkillId] = target.Id;
            }
        }

        return index;
    }

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

/// <summary>
/// Remodel Confirm shares a start skill across every design in a pack. The extra u32 is the
/// housing template the player picked; SkillStarted must not echo flag 7 (that flag is also
/// grade-enchant, and the enchanting extra desyncs the stream).
/// </summary>
public static class HousingRebuildSkillCast
{
    /// <summary>The housing template Confirm asked the house to become, or 0 when the extra is missing.</summary>
    public static uint RequestedHousingId(SkillObject skillObject) =>
        skillObject is SkillObjectHousingRebuild rebuild ? rebuild.HousingId : 0;

    /// <summary>The object SkillStarted may echo: flag None, housing id kept for the rebuild effect.</summary>
    public static SkillObject ForSkillStarted(uint housingId) =>
        housingId == 0 ? new SkillObject() : new SkillObjectHousingRebuild { HousingId = housingId };
}
