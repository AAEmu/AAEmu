using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// The three siege attributes through the real <see cref="DamageEffect"/> path: <c>siege_dps</c> (260) and
/// <c>siege_damage_mul</c> (261) off the caster and <c>incoming_siege_damage_mul</c> (149) off the victim.
/// </summary>
/// <remarks>
/// The caster is a plain <see cref="Unit"/> whose level damage is the only damage source; with no level
/// variance and no weapon slot, <c>LevelDps * LevelMd</c> plus the 0.5 of the level formula composes to
/// exactly 100 and <c>Random.Next(min, max)</c> over an empty range returns it unchanged, so every number
/// below is an exact integer before a multiplier touches it. The victim is a player, because 149 is composed
/// by <c>Character</c>, with the formula-backed stats pinned so no formula table is needed.
/// </remarks>
public class SiegeDamageAttributeTests
{
    private const int ComposedDamage = 100;
    private const float LevelDpsForExactDamage = 199f; // 199 * 0.5f = 99.5f, + 0.5f = 100f
    private const float LevelMdForExactDamage = 0.5f;
    private const int TargetStartHp = 20_000;

    [Test]
    public async Task WithoutAnySiegeRow_TheHitIsUnchanged()
    {
        // The exact-equality pin for 149/260/261 at once: the composed hit is 100 and stays 100.
        var caster = CreateCaster();
        var victim = NewVictim();

        await Assert.That(caster.SiegeDps).IsEqualTo(0);
        await Assert.That(caster.SiegeDamageMul).IsEqualTo(1f);
        await Assert.That(victim.IncomingSiegeDamageMul).IsEqualTo(1f);

        Hit(caster, victim);

        await Assert.That(TargetStartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task SiegeDamageMul_ScalesTheCastersHit()
    {
        // The 검은 가시 감옥 stages store +700 as their strongest row: +70%.
        var caster = CreateCaster((UnitAttribute.SiegeDamageMul, 700L));
        var victim = NewVictim();

        await Assert.That(caster.SiegeDamageMul).IsEqualTo(1.7f);

        Hit(caster, victim);

        await Assert.That(TargetStartHp - victim.Hp).IsEqualTo(170);
    }

    [Test]
    public async Task SiegeDps_AddsTheCastersOwnDamage()
    {
        // dpsInc enters as max += dpsInc * 0.001f, the term spell_dps already feeds for Magic.
        var caster = CreateCaster((UnitAttribute.SiegeDps, 200_000L));
        var victim = NewVictim();

        await Assert.That(caster.SiegeDps).IsEqualTo(200_000);

        Hit(caster, victim);

        await Assert.That(TargetStartHp - victim.Hp).IsEqualTo(300);
    }

    [Test]
    public async Task SiegeDamageMul_IsNotUsedByTheOtherDamageTypes()
    {
        var caster = CreateCaster((UnitAttribute.SiegeDamageMul, 700L));
        var victim = NewVictim();

        Hit(caster, victim, DamageType.Melee);

        await Assert.That(TargetStartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task IncomingSiegeDamageMul_ScalesWhatTheVictimTakes()
    {
        // Buff 17128 stores -800 for "받는 공성 피해율이 80% 감소".
        var caster = CreateCaster();
        var victim = NewVictim();
        TestUnitModifier.Apply(victim, UnitAttribute.IncomingSiegeDamageMul, -800);

        await Assert.That(victim.IncomingSiegeDamageMul).IsEqualTo(0.2f);

        Hit(caster, victim);

        await Assert.That(TargetStartHp - victim.Hp).IsEqualTo(20);
    }

    [Test]
    public async Task IncomingSiegeDamageMul_AtMinus1000_MakesTheVictimImmune()
    {
        // Buff 14857 stores -1000; the composition stops at 0 instead of turning the hit into a heal.
        var caster = CreateCaster();
        var victim = NewVictim();
        TestUnitModifier.Apply(victim, UnitAttribute.IncomingSiegeDamageMul, -1000);

        await Assert.That(victim.IncomingSiegeDamageMul).IsEqualTo(0f);

        Hit(caster, victim);

        await Assert.That(TargetStartHp - victim.Hp).IsEqualTo(0);
    }

    [Test]
    public async Task IncomingSiegeDamageMul_BelowMinus1000_StaysAtZero()
    {
        // Buff 24837 (수호탑의 보호) stores -7000 and buff 24950 -21000.
        var victim = NewVictim();
        TestUnitModifier.Apply(victim, UnitAttribute.IncomingSiegeDamageMul, -7000);

        await Assert.That(victim.IncomingSiegeDamageMul).IsEqualTo(0f);
    }

    [Test]
    public async Task AVictimsOwnOffensiveRow_DoesNotMoveTheHit()
    {
        // 260/261 belong to the caster; a buff on the victim must not scale what it takes.
        var caster = CreateCaster();
        var victim = NewVictim();
        TestUnitModifier.Apply(victim, UnitAttribute.SiegeDamageMul, 700L);

        Hit(caster, victim);

        await Assert.That(TargetStartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    private static TestPlayer NewVictim() => new() { ObjId = 201, Level = 50, Hp = TargetStartHp };

    private static Unit CreateCaster(params (UnitAttribute Attribute, long Value)[] rows)
    {
        var caster = new Unit
        {
            ObjId = 100,
            Level = 50,
            Hp = TargetStartHp,
            MaxHp = TargetStartHp,
            LevelDps = LevelDpsForExactDamage
        };

        for (var i = 0; i < rows.Length; i++)
            TestUnitModifier.Apply(caster, rows[i].Attribute, rows[i].Value, buffIndex: (uint)(i + 1));

        return caster;
    }

    private static void Hit(Unit caster, Unit victim, DamageType damageType = DamageType.Siege)
    {
        var effect = new DamageEffect
        {
            Id = 1,
            DamageType = damageType,
            Multiplier = 1f,
            DpsIncMultiplier = 1f,
            UseLevelDamage = true,
            LevelMd = LevelMdForExactDamage,
            WeaponSlotId = -1
        };

        effect.Apply(
            caster,
            new SkillCasterUnit(caster.ObjId),
            victim,
            new SkillCastUnitTarget(victim.ObjId),
            new CastSkill(1, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);
    }

    /// <summary>
    /// A player victim with the formula-backed stats pinned, so the siege branch and the melee cross-check
    /// need no formula tables. <c>Flexibility</c> is read by the critical roll for every damage type.
    /// </summary>
    private sealed class TestPlayer : CharacterMock
    {
        public override int Armor => 0;
        public override int MagicResistance => 0;
        public override int Flexibility => 0;
    }
}
