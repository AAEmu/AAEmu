using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Utils.Mocks;

/// <summary>
/// Puts one <c>unit_modifiers</c> row on a unit the way the content does: through
/// <see cref="BuffTemplate.Start"/>, which is where a buff's rows become bonuses (<c>SkillManager</c> loads
/// the <c>owner_type='Buff'</c> rows into <c>BuffTemplate.Bonuses</c>). Tests that check what consumes such
/// a row use this rather than <c>Unit.AddBonus</c> directly, so the real buff plumbing is on the path.
/// </summary>
/// <remarks>
/// Sibling of the mock the C2a branch (fix/c2a-anti-npc-damage-muls) adds under the name
/// <c>TestBuffModifier</c>; this one keeps a distinct name so the two branches can land in either order
/// without a duplicate class.
/// </remarks>
public static class TestUnitModifier
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

        // Start() reads SkillManager.Instance for the skills the buff grants, and SkillManager has no
        // parameterless constructor, so the singleton has to be registered around the call.
        using var skillsScope = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());

        template.Start(owner, owner, buff);
    }
}
