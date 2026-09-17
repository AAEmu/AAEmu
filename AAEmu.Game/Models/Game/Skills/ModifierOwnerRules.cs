namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which row of <c>buff_modifiers</c> / <c>skill_modifiers</c> owns a modifier, i.e. what has to be carrying it
/// for it to apply.
/// </summary>
public enum ModifierOwner
{
    /// <summary>owner_type='Buff': the row applies while the buff whose id is the row's owner_id is on the unit.</summary>
    Buff,

    /// <summary>owner_type='Item': the row applies while the item whose template id is the row's owner_id is equipped.</summary>
    Item,

    /// <summary>owner_type='ExpeditionBuffGrade': the row applies at the expedition buff grade the owner_id names.</summary>
    ExpeditionBuffGrade,

    /// <summary>owner_type='CombatResource': the row applies while the unit carries the combat resource the owner_id names.</summary>
    CombatResource,

    /// <summary>An owner_type no content carries yet; the row is counted once rather than filed under a buff id.</summary>
    Unknown
}

/// <summary>
/// The one decision the two modifier loaders share: an <c>owner_id</c> is only a buff id for rows whose
/// <c>owner_type</c> says so.
/// </summary>
/// <remarks>
/// Both tables are keyed by <c>owner_id</c> alone in this server, and both loaders filed every row under it
/// regardless of <c>owner_type</c>. 115 <c>buff_modifiers</c> rows and 172 <c>skill_modifiers</c> rows are
/// owned by an Item, whose owner_id is an <c>items</c> template id, and 21 more are owned by an expedition
/// buff grade; whenever one of those ids coincides with a buff id the row was granted by that buff — and,
/// through <c>SkillModifiers.AddModifiers</c>, stayed on the unit for as long as the unrelated buff lasted.
/// </remarks>
public static class ModifierOwnerRules
{
    public static ModifierOwner Classify(string ownerType) => ownerType switch
    {
        "Buff" => ModifierOwner.Buff,
        "Item" => ModifierOwner.Item,
        "ExpeditionBuffGrade" => ModifierOwner.ExpeditionBuffGrade,
        "CombatResource" => ModifierOwner.CombatResource,
        _ => ModifierOwner.Unknown
    };
}
