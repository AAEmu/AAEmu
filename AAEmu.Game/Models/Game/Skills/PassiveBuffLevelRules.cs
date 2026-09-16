using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The level gate on a learned passive: <c>passive_buffs.level</c> and <c>passive_buffs.active</c>. Both
/// columns were loaded and never read, so a client could ask for any passive in the table and get it —
/// <c>CSLearnBuffPacket</c> carries nothing but the id — and nothing ever re-evaluated the passives a
/// character holds when their level or ability level moved.
/// </summary>
/// <remarks>
/// Counted from the 10.0.2.13 content DB (279 <c>passive_buffs</c> rows): 268 rows carry level 1, the
/// remaining 11 carry 40 (2 rows), 45 (2), 50 (3), 55 (2) and 60 (2), so the gate is about the two
/// ability-tree chains 28/29 (Predator and Trooper, passives 293 and 315-321, 335) plus the two level-60
/// general passives 8 and 18. Five rows are <c>active='t'</c> (101, 347, 360, 363, 369) — the passives the
/// game hands out by itself.
/// </remarks>
public static class PassiveBuffLevelRules
{
    /// <summary>
    /// Whether a character of <paramref name="characterLevel"/> has reached a passive's own level. The
    /// 268 rows at level 1 pass for every character; 0 is treated as no requirement, which is what the
    /// four rows that ship it would mean either way.
    /// </summary>
    public static bool MeetsLevel(byte requiredLevel, int characterLevel) => characterLevel >= requiredLevel;

    /// <summary>Whether a learn request for <paramref name="template"/> may be honoured yet.</summary>
    public static bool CanLearn(PassiveBuffTemplate template, int characterLevel) =>
        template != null && MeetsLevel(template.Level, characterLevel);

    /// <summary>
    /// The <c>active='t'</c> passives a character is old enough for and does not hold yet, ascending by id
    /// so that a level-up grants them in a stable order. Whether one may actually be learned is still the
    /// caller's business: <c>CharacterSkills.AddBuff</c> applies the ability-tree and point requirements to
    /// these the same way it does to a client request.
    /// </summary>
    public static IReadOnlyList<uint> AutoGranted(
        IEnumerable<PassiveBuffTemplate> templates, int characterLevel, ICollection<uint> learned)
    {
        if (templates == null)
            return [];

        var granted = new SortedSet<uint>();
        foreach (var template in templates)
        {
            if (template == null || !template.Active || !MeetsLevel(template.Level, characterLevel))
                continue;
            if (learned != null && learned.Contains(template.Id))
                continue;
            granted.Add(template.Id);
        }

        return granted.ToList();
    }

    /// <summary>
    /// The ability level a passive's bonuses are scaled by. A passive's buff was applied with the
    /// <c>Buff.AbLevel</c> default of 1, so every <c>linear_level_bonus</c> row it carries contributed
    /// its one-hundredth; 19 such rows sit on 14 general passives (row 22322: value 22, linear 584) and
    /// scale with the character level, which is what <c>Character.GetAbLevel(General)</c> returns.
    /// </summary>
    public static uint AbLevelFor(int abilityLevel) => (uint)Math.Max(1, abilityLevel);
}
