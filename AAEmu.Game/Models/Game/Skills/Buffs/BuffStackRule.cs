namespace AAEmu.Game.Models.Game.Skills.Buffs;

/// <summary>
/// <c>buffs.stack_rule_id</c>, decoded from <c>enum_buff_stack_rule</c> (seven rows in 10.0.2.13).
/// </summary>
/// <remarks>
/// The members must stay at the ids the content table ships: the loader casts the column straight to
/// this enum (<c>SkillManager</c>, the <c>buffs</c> reader), so a missing member silently becomes an
/// undefined value and the buff falls to <c>Buffs.AddBuff</c>'s default branch. Rule 7 is that case —
/// <c>multiple_decrease_one</c>, 28 rows — and was undefined until it was added here.
/// </remarks>
public enum BuffStackRule
{
    Refresh = 1,
    ChargeRefresh = 2,
    ChargeExtend = 3,
    Multiple = 4,
    Extend = 5,
    Independent = 6,
    MultipleDecreaseOne = 7
}
