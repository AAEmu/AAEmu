using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class DamageMultiplierRulesTests
{
    /// <summary>Six different factors so a wrong pick cannot pass by coincidence.</summary>
    private static AntiKindDamageMultipliers Distinct() => new(2f, 3f, 4f, 5f, 6f, 7f);

    [Test]
    public async Task ClassifyVictim_NpcIsNpcKind()
    {
        await Assert.That(DamageMultiplierRules.ClassifyVictim(new Npc())).IsEqualTo(DamageVictimKind.Npc);
    }

    [Test]
    public async Task ClassifyVictim_CharacterIsPlayerKind()
    {
        await Assert.That(DamageMultiplierRules.ClassifyVictim(new Character(new UnitCustomModelParams())))
            .IsEqualTo(DamageVictimKind.Player);
    }

    [Test]
    public async Task ClassifyVictim_AnythingElseIsOther()
    {
        // Pets, summons and mates are neither of the two kinds the content splits on, and the DB carries
        // no anti-pet/anti-slave trio for them.
        await Assert.That(DamageMultiplierRules.ClassifyVictim(new Unit())).IsEqualTo(DamageVictimKind.Other);
        await Assert.That(DamageMultiplierRules.ClassifyVictim(new Slave())).IsEqualTo(DamageVictimKind.Other);
    }

    [Test]
    public async Task SelectDamageMultiplier_NpcVictimTakesTheAntiNpcTrio()
    {
        var multipliers = Distinct();

        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Npc, DamageType.Melee, multipliers)).IsEqualTo(2f);
        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Npc, DamageType.Ranged, multipliers)).IsEqualTo(3f);
        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Npc, DamageType.Magic, multipliers)).IsEqualTo(4f);
    }

    [Test]
    public async Task SelectDamageMultiplier_PlayerVictimTakesTheAntiPcTrio()
    {
        var multipliers = Distinct();

        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Player, DamageType.Melee, multipliers)).IsEqualTo(5f);
        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Player, DamageType.Ranged, multipliers)).IsEqualTo(6f);
        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Player, DamageType.Magic, multipliers)).IsEqualTo(7f);
    }

    [Test]
    public async Task SelectDamageMultiplier_OtherVictim_IsNeutralForEveryDamageType()
    {
        foreach (var damageType in Enum.GetValues<DamageType>())
        {
            await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
                DamageVictimKind.Other, damageType, Distinct())).IsEqualTo(1.0f);
        }
    }

    [Test]
    public async Task SelectDamageMultiplier_SiegeAndHealTypes_AreNeutral()
    {
        // Neither has an anti-kind attribute in the 10.0.2.13 table; the existing DamageEffect switch
        // gives siege 1.0f with a TODO, and Heal is not a damage type at all.
        foreach (var victimKind in Enum.GetValues<DamageVictimKind>())
        {
            await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
                victimKind, DamageType.Siege, Distinct())).IsEqualTo(1.0f);
            await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
                victimKind, DamageType.Heal, Distinct())).IsEqualTo(1.0f);
        }
    }

    [Test]
    public async Task SelectDamageMultiplier_WithoutAnyBonus_IsExactlyOneEverywhere()
    {
        // The promise that nothing moves for a caster with none of these rows: every victim kind and
        // every damage type has to come out as exactly 1.0f, not 0.9999999f.
        foreach (var victimKind in Enum.GetValues<DamageVictimKind>())
        {
            foreach (var damageType in Enum.GetValues<DamageType>())
            {
                await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
                    victimKind, damageType, AntiKindDamageMultipliers.None)).IsEqualTo(1.0f);
            }
        }
    }

    [Test]
    public async Task SelectDamageMultiplier_BelowMinusOneThousand_IsFlooredAtZero()
    {
        // 위압감 (buff 2102/2104/25747/25907) carries -1500 on each anti-NPC id: -0.5 composed. A
        // negative factor would flip the damage into a heal, so it stops at zero instead.
        var intimidation = new AntiKindDamageMultipliers(-0.5f, -0.5f, -0.5f, 1f, 1f, 1f);

        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Npc, DamageType.Melee, intimidation)).IsEqualTo(0f);
        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Npc, DamageType.Magic, intimidation)).IsEqualTo(0f);
        await Assert.That(DamageMultiplierRules.SelectDamageMultiplier(
            DamageVictimKind.Player, DamageType.Melee, intimidation)).IsEqualTo(1.0f);
    }

    [Test]
    public async Task From_UnitWithoutBonuses_IsNone()
    {
        var multipliers = AntiKindDamageMultipliers.From(new Unit());

        await Assert.That(multipliers).IsEqualTo(AntiKindDamageMultipliers.None);
        await Assert.That(multipliers.MeleeAntiNpc).IsEqualTo(1.0f);
        await Assert.That(multipliers.RangedAntiNpc).IsEqualTo(1.0f);
        await Assert.That(multipliers.SpellAntiNpc).IsEqualTo(1.0f);
        await Assert.That(multipliers.MeleeAntiPc).IsEqualTo(1.0f);
        await Assert.That(multipliers.RangedAntiPc).IsEqualTo(1.0f);
        await Assert.That(multipliers.SpellAntiPc).IsEqualTo(1.0f);
    }

    [Test]
    public async Task From_UnitWithBuffRows_ReadsTheComposedAttributes()
    {
        var unit = new Unit { ObjId = 1 };
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMulAntiNpc, 1000, buffIndex: 1);
        TestBuffModifier.Apply(unit, UnitAttribute.RangedDamageMulAntiNpc, 2000, buffIndex: 2);
        TestBuffModifier.Apply(unit, UnitAttribute.SpellDamageMulAntiNpc, 3000, buffIndex: 3);
        TestBuffModifier.Apply(unit, UnitAttribute.MeleeDamageMulAntiPc, 4000, buffIndex: 4);
        TestBuffModifier.Apply(unit, UnitAttribute.RangedDamageMulAntiPc, 5000, buffIndex: 5);
        TestBuffModifier.Apply(unit, UnitAttribute.SpellDamageMulAntiPc, 6000, buffIndex: 6);

        var multipliers = AntiKindDamageMultipliers.From(unit);

        await Assert.That(multipliers.MeleeAntiNpc).IsEqualTo(2f);
        await Assert.That(multipliers.RangedAntiNpc).IsEqualTo(3f);
        await Assert.That(multipliers.SpellAntiNpc).IsEqualTo(4f);
        await Assert.That(multipliers.MeleeAntiPc).IsEqualTo(5f);
        await Assert.That(multipliers.RangedAntiPc).IsEqualTo(6f);
        await Assert.That(multipliers.SpellAntiPc).IsEqualTo(7f);
    }
}
