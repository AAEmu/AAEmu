using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// The seven attributes this change starts consuming: the anti-NPC trio (<c>enum_unit_attribute</c>
/// 196-198), the anti-PC trio (244-246) and <c>heal_damage_mul</c> (222). All are authored as a per-mille
/// delta from 0 - <c>unit_modifiers</c> rows for them run from -1500 to 2000 - which is the convention
/// <c>Character.MeleeDamageMul</c> and <c>Character.HealMul</c> already compose, so the getters here are
/// pinned against those siblings and against an absent bonus being exactly 1.0f.
/// </summary>
public class UnitAntiKindMultiplierAttributeTests
{
    private static CharacterMock CreateUnit() => new() { ObjId = 1, Hp = 1000, MaxHp = 1000 };

    [Test]
    public async Task NoBonus_AllSevenAreExactlyOne()
    {
        var unit = CreateUnit();

        await Assert.That(unit.MeleeDamageMulAntiNpc).IsEqualTo(1.0f);
        await Assert.That(unit.RangedDamageMulAntiNpc).IsEqualTo(1.0f);
        await Assert.That(unit.SpellDamageMulAntiNpc).IsEqualTo(1.0f);
        await Assert.That(unit.MeleeDamageMulAntiPc).IsEqualTo(1.0f);
        await Assert.That(unit.RangedDamageMulAntiPc).IsEqualTo(1.0f);
        await Assert.That(unit.SpellDamageMulAntiPc).IsEqualTo(1.0f);
        await Assert.That(unit.HealDamageMul).IsEqualTo(1.0f);
    }

    [Test]
    public async Task AValueRowOf1000_Doubles_AndTheShippedExtremesFollow()
    {
        // unit_modifiers rows for these ids carry per-mille deltas: 1000 = +100%, 2000 = +200%
        // (buff 27590 "pvp 기술 피해 버프" on melee_damage_mul_anti_pc), -1500 and -900 the extremes of the
        // buff rows on the anti-NPC trio. -1500 composes to a negative factor (the 위압감 buffs); the
        // getter reports the composition faithfully and DamageMultiplierRules is what floors it at 0.
        var unit = CreateUnit();
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMulAntiNpc, 1000);
        TestBuffModifier.Apply(unit, UnitAttribute.SpellDamageMulAntiNpc, 2000);
        TestBuffModifier.Apply(unit, UnitAttribute.RangedDamageMulAntiNpc, -1500);
        TestBuffModifier.Apply(unit, UnitAttribute.HealDamageMul, -900);

        await Assert.That(unit.MeleeDamageMulAntiNpc).IsEqualTo(2.0f);
        await Assert.That(unit.SpellDamageMulAntiNpc).IsEqualTo(3.0f);
        await Assert.That(unit.RangedDamageMulAntiNpc).IsEqualTo(-0.5f);
        await Assert.That(unit.HealDamageMul).IsEqualTo(0.1f);
    }

    [Test]
    public async Task TwoRowsOnTheSameAttribute_AddUp()
    {
        var unit = CreateUnit();
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMulAntiPc, 1000, buffIndex: 1);
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMulAntiPc, 500, buffIndex: 2);

        await Assert.That(unit.MeleeDamageMulAntiPc).IsEqualTo(2.5f);
    }

    [Test]
    public async Task EachAttributeIsIndependent()
    {
        var unit = CreateUnit();
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMulAntiNpc, 1000);

        await Assert.That(unit.MeleeDamageMulAntiNpc).IsEqualTo(2.0f);
        await Assert.That(unit.RangedDamageMulAntiNpc).IsEqualTo(1.0f);
        await Assert.That(unit.SpellDamageMulAntiNpc).IsEqualTo(1.0f);
        await Assert.That(unit.MeleeDamageMulAntiPc).IsEqualTo(1.0f);
        await Assert.That(unit.RangedDamageMulAntiPc).IsEqualTo(1.0f);
        await Assert.That(unit.SpellDamageMulAntiPc).IsEqualTo(1.0f);
        await Assert.That(unit.HealDamageMul).IsEqualTo(1.0f);
    }

    [Test]
    public async Task AValueRow_ComposesLikeTheSiblingDamageAndHealMuls()
    {
        // The whole reason the getters use a base of 0 plus the 1000 baseline: it is the shape of the
        // attributes these sit next to, so equal rows have to produce equal factors.
        var unit = CreateUnit();
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMul, 1000, buffIndex: 1);
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMulAntiNpc, 1000, buffIndex: 2);
        TestBuffModifier.Apply(unit, UnitAttribute.HealMul, 2500, buffIndex: 3);
        TestBuffModifier.Apply(unit, UnitAttribute.HealDamageMul, 2500, buffIndex: 4);

        await Assert.That(unit.MeleeDamageMul).IsEqualTo(2.0f);
        await Assert.That(unit.MeleeDamageMulAntiNpc).IsEqualTo(unit.MeleeDamageMul);
        await Assert.That(unit.HealMul).IsEqualTo(3.5f);
        await Assert.That(unit.HealDamageMul).IsEqualTo(unit.HealMul);
    }

    [Test]
    public async Task APercentRow_IsInert_AsItIsForTheSiblingMuls()
    {
        // Three buff rows in the DB are type=percent instead of type=value on the anti-NPC trio (buff
        // 31679 "PvE피해량 10배" is one, with 5000). Unit.CalculateWithBonuses scales a percent row
        // against the value it has composed so far, and these attributes compose from 0, so the row adds
        // nothing - exactly as the 17 percent rows on melee_damage_mul already behave. Pinned here so a
        // change to that shared convention has to come with a deliberate update.
        var unit = CreateUnit();
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMulAntiNpc, 5000, UnitModifierType.Percent);

        await Assert.That(unit.MeleeDamageMulAntiNpc).IsEqualTo(1.0f);
    }
}
