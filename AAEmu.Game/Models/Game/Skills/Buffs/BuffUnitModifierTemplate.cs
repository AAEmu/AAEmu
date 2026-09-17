using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

/// <summary>
/// One row of <c>buff_unit_modifiers</c> (160 rows in 10.0.2.13): the buff named by the row's
/// <c>owner_id</c> contributes the <c>unit_modifiers</c> rows of owner_type='BuffUnitModifier' whose
/// owner_id is this selector's id — but only to units that carry <see cref="TagId"/> (or
/// <see cref="BuffId"/>), not to every unit the buff lands on.
/// </summary>
/// <remarks>
/// Example: selector 40 is owned by buff 15793 and names buff 2675 (Dash), and its modifier is attribute 10
/// (move_speed_mul) +300, i.e. "while 15793 is on you, if you are dashing you move 30% faster".
/// </remarks>
public class BuffUnitModifierTemplate
{
    public uint Id { get; set; }

    /// <summary>The buff tag the unit has to carry, or 0 when the row keys on <see cref="BuffId"/> instead.</summary>
    public uint TagId { get; set; }

    /// <summary>The buff the unit has to carry, or 0 when the row keys on <see cref="TagId"/> instead.</summary>
    public uint BuffId { get; set; }

    /// <summary>The unit_modifiers rows of owner_type='BuffUnitModifier' whose owner_id is <see cref="Id"/>.</summary>
    public List<BonusTemplate> Bonuses { get; } = [];

    /// <summary>Whether <paramref name="unit"/> is one of the units this selector applies to.</summary>
    public bool Matches(BaseUnit unit)
    {
        if (unit == null)
            return false;

        return (TagId > 0 && unit.Buffs.CheckBuffTag(TagId))
               || (BuffId > 0 && unit.Buffs.CheckBuff(BuffId));
    }
}
