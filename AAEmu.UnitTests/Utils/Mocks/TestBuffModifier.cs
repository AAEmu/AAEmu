using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Utils.Mocks;

/// <summary>
/// Puts one <c>unit_modifiers</c> row on a unit the way the content does: through
/// <see cref="BuffTemplate.Start"/>, which is where a buff's rows are turned into bonuses
/// (<c>SkillManager</c> loads <c>owner_type='Buff'</c> rows into <c>BuffTemplate.Bonuses</c>). Tests that
/// check what consumes such a row use this rather than <c>Unit.AddBonus</c> directly so the real buff
/// plumbing is on the path.
/// </summary>
public static class TestBuffModifier
{
    public static void Apply(
        Unit owner,
        UnitAttribute attribute,
        long value,
        UnitModifierType modifierType = UnitModifierType.Value,
        uint buffIndex = 1)
    {
        var template = new BuffTemplate();
        template.Bonuses.Add(new BonusTemplate
        {
            Attribute = attribute,
            ModifierType = modifierType,
            Value = value
        });

        var buff = new Buff(owner, owner, new SkillCasterUnit(owner.ObjId), template, null, DateTime.UtcNow)
        {
            Index = buffIndex,
            Passive = true, // skip the SCBuffCreatedPacket broadcast inside Start()
            AbLevel = 1
        };

        template.Start(owner, owner, buff);
    }
}
