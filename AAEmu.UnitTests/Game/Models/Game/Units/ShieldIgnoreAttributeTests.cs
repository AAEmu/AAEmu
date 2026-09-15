using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// <c>ignore_shield_chance</c> (204) through the real <see cref="Unit.ReduceCurrentHp"/> absorption path.
/// </summary>
/// <remarks>
/// The victim carries one real absorption buff (charge <see cref="ShieldCharge"/>), the shape
/// <c>buffs.damage_absorption_type_id = 2</c> ships for 보호막 / 신의 가호. A bypassing hit must leave that
/// charge untouched and take the full damage off health; a non-bypassing one must spend the charge first.
/// </remarks>
[NotInParallel]
public class ShieldIgnoreAttributeTests
{
    private const int ShieldCharge = 500;
    private const int HitDamage = 100;
    private const int StartHp = 10_000;

    [Test]
    public async Task WithoutARow_TheShieldAbsorbsTheHit()
    {
        // The absence pin: a charge of 500 eats the whole 100 and health never moves.
        var attacker = new Unit { ObjId = 1, Level = 50 };
        var (victim, shield) = NewVictimWithShield();

        await Assert.That(attacker.IgnoreShieldChance).IsEqualTo(0L);

        victim.ReduceCurrentHp(attacker, HitDamage);

        await Assert.That(shield.Charge).IsEqualTo(ShieldCharge - HitDamage);
        await Assert.That(victim.Hp).IsEqualTo(StartHp);
    }

    [Test]
    public async Task ACertainBypass_LeavesTheShieldAlone_AndTakesTheFullHit()
    {
        // Item 50774's (attr_test) 204 row stores 100 and buff 8227 (양손 무기 착용) 500 for the
        // "방패 관통률 50% 증가" its description spells out; 1000 is the certain end of that scale.
        var attacker = new Unit { ObjId = 1, Level = 50 };
        TestUnitModifier.Apply(attacker, UnitAttribute.IgnoreShieldChance, 1000);
        var (victim, shield) = NewVictimWithShield();

        await Assert.That(attacker.IgnoreShieldChance).IsEqualTo(1000L);

        victim.ReduceCurrentHp(attacker, HitDamage);

        await Assert.That(shield.Charge).IsEqualTo(ShieldCharge);
        await Assert.That(victim.Hp).IsEqualTo(StartHp - HitDamage);
    }

    [Test]
    public async Task ANegativeRow_NeverBypasses()
    {
        // unit_attribute_limits row 26 floors 204 at 0; the one shipped negative row (buff 11194) must
        // therefore read as "no chance" rather than as a second shield for the victim. Five hits of 100
        // drain the 500 charge exactly and never reach health.
        var attacker = new Unit { ObjId = 1, Level = 50 };
        TestUnitModifier.Apply(attacker, UnitAttribute.IgnoreShieldChance, -1000);
        var (victim, shield) = NewVictimWithShield();

        for (var i = 0; i < 5; i++)
            victim.ReduceCurrentHp(attacker, HitDamage);

        await Assert.That(shield.Charge).IsEqualTo(0);
        await Assert.That(victim.Hp).IsEqualTo(StartHp);
    }

    [Test]
    public async Task AVictimsOwnRow_DoesNotBypassItsOwnShield()
    {
        // The attribute is the attacker's: a 204 row on the victim must not open its own shield.
        var attacker = new Unit { ObjId = 1, Level = 50 };
        var (victim, shield) = NewVictimWithShield();
        TestUnitModifier.Apply(victim, UnitAttribute.IgnoreShieldChance, 1000);

        victim.ReduceCurrentHp(attacker, HitDamage);

        await Assert.That(shield.Charge).IsEqualTo(ShieldCharge - HitDamage);
    }

    private static (Unit Victim, Buff Shield) NewVictimWithShield()
    {
        var victim = new Unit { ObjId = 2, Level = 50, Hp = StartHp, MaxHp = StartHp };

        var template = new BuffTemplate { Id = 95, DamageAbsorptionTypeId = 2 };
        var shield = new Buff(victim, victim, new SkillCasterUnit(victim.ObjId), template, null, DateTime.UtcNow)
        {
            Index = 1,
            Charge = ShieldCharge,
            Passive = true // no SCBuffUpdatedPacket broadcast when the charge moves
        };

        var buffs = Mock.Of<IBuffs>();
        buffs.GetAbsorptionEffects().Returns([shield]);
        victim.Buffs = buffs.Object;

        return (victim, shield);
    }
}
