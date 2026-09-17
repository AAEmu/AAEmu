using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <c>use_percent_damage</c> through the real <see cref="DamageEffect"/> path: a percentage of one of the two
/// units' pools added to the composed hit, and the byte-identical behaviour of a row that does not set the
/// flag.
/// </summary>
/// <remarks>
/// Same harness as <see cref="AntiKindDamageMultiplierTests"/>: a plain <see cref="Unit"/> caster whose only
/// damage source is level damage, which composes exactly <see cref="ComposedDamage"/>, against a victim
/// pinned to armour 0. Everything here is integral, so a wrong percentage cannot hide inside a rounding.
/// </remarks>
[NotInParallel]
public class PercentDamageEffectTests
{
    private const int ComposedDamage = 100;
    private const float LevelDpsForExactDamage = 199f; // 199 * 0.5f = 99.5f, + 0.5f = 100f
    private const float LevelMdForExactDamage = 0.5f;
    private const int VictimMaxHp = 20_000;
    private const int CasterMaxHp = 40_000;

    private EmptySkillManagerScope _skillManagerScope;

    [Before(Test)]
    public void InstallEmptySkillManager() => _skillManagerScope = new EmptySkillManagerScope();

    [After(Test)]
    public void RestoreSkillManager() => _skillManagerScope?.Dispose();

    [Test]
    public async Task WithoutTheFlag_TheHitIsExactlyWhatItWas()
    {
        // 10,510 of the 11,001 damage_effects rows. The percentage block is skipped entirely, so the hit is
        // the composed 100 it has always been.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            PercentMin = 30,
            PercentMax = 40,
            PercentDamageResourceTypeId = (int)PercentDamageResourceType.MaxHealth,
            UseCurrentHealth = true
        });

        await Assert.That(VictimMaxHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task TenPercentOfTheVictimsMaximumHealth_AddsTenPercent()
    {
        // The acceptance case: a 10..10 % row on a 20,000 HP victim adds exactly 2,000 to the 100 composed.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            UsePercentDamage = true,
            PercentMin = 10,
            PercentMax = 10,
            PercentDamageResourceTypeId = (int)PercentDamageResourceType.MaxHealth
        });

        await Assert.That(VictimMaxHp - victim.Hp).IsEqualTo(ComposedDamage + 2_000);
    }

    [Test]
    public async Task SourceHealth_ReadsTheCastersMaximumHealth()
    {
        // Skill 44265 방패 휘두르기: 바위 ("자신의 최대 생명력 1%~2%만큼의 추가 피해") with use_source_health
        // set: 2 % of the caster's 40,000 is 800, not the victim's 400.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            UsePercentDamage = true,
            PercentMin = 2,
            PercentMax = 2,
            PercentDamageResourceTypeId = (int)PercentDamageResourceType.MaxHealth,
            UseCurrentHealth = true
        });

        await Assert.That(VictimMaxHp - victim.Hp).IsEqualTo(ComposedDamage + 800);
    }

    [Test]
    public async Task CurrentHealth_ReadsWhatIsLeft()
    {
        // damage_effects 7609 (자폭하기): 50 % of what the victim has left, not of its maximum.
        var caster = CreateCaster();
        var victim = CreateVictim();
        victim.Hp = 4_000;

        Hit(caster, victim, new DamageEffect
        {
            UsePercentDamage = true,
            PercentMin = 50,
            PercentMax = 50,
            PercentDamageResourceTypeId = (int)PercentDamageResourceType.CurrentHealth
        });

        await Assert.That(4_000 - victim.Hp).IsEqualTo(ComposedDamage + 2_000);
    }

    [Test]
    public async Task TheAuthoredMultiplier_DoesNotRescaleThePercentage()
    {
        // damage_effects 861 pairs a 35 % share with multiplier 300. The share is added after the range has
        // been composed, so it stays 35 % of the victim's health on top of the composed hit. The victim is
        // big enough to survive the 30,000 that multiplier authors, because a lethal hit leaves through
        // Unit.DoDie, which needs the world manager this unit test has no DI for.
        const int bigVictimMaxHp = 200_000;
        var caster = CreateCaster();
        var victim = CreateVictim(bigVictimMaxHp);

        Hit(caster, victim, new DamageEffect
        {
            Multiplier = 300f,
            UsePercentDamage = true,
            PercentMin = 35,
            PercentMax = 35,
            PercentDamageResourceTypeId = (int)PercentDamageResourceType.MaxHealth
        });

        await Assert.That(bigVictimMaxHp - victim.Hp).IsEqualTo(ComposedDamage * 300 + 70_000);
    }

    [Test]
    public async Task TheBandIsRolledPerHit()
    {
        // A 0..10 % band on a 200,000 HP victim must stay inside its own bounds over many rolls, and it must
        // be able to move: the smallest share is 0 (the composed hit alone) and the largest is a tenth of the
        // pool. The victim is sized so no roll can be lethal — a kill leaves through Unit.DoDie, which needs
        // the world manager this unit test has no DI for.
        const int bigVictimMaxHp = 200_000;
        var caster = CreateCaster();

        var sawMore = false;
        var sawLess = false;
        for (var i = 0; i < 40; i++)
        {
            var victim = CreateVictim(bigVictimMaxHp);
            Hit(caster, victim, new DamageEffect
            {
                UsePercentDamage = true,
                PercentMin = 0,
                PercentMax = 10,
                PercentDamageResourceTypeId = (int)PercentDamageResourceType.MaxHealth
            });

            var dealt = bigVictimMaxHp - victim.Hp;
            await Assert.That(dealt).IsGreaterThanOrEqualTo(ComposedDamage);
            await Assert.That(dealt).IsLessThanOrEqualTo(ComposedDamage + bigVictimMaxHp / 10);

            sawMore |= dealt > ComposedDamage + 5_000;
            sawLess |= dealt < ComposedDamage + 15_000;
        }

        await Assert.That(sawMore).IsTrue();
        await Assert.That(sawLess).IsTrue();
    }

    [Test]
    public async Task AnEmptyPool_AddsNothingOnTop()
    {
        // A row that rolled 0 % is the composed hit alone, and a dead victim's current health is 0.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            UsePercentDamage = true,
            PercentMin = 0,
            PercentMax = 0,
            PercentDamageResourceTypeId = (int)PercentDamageResourceType.MaxHealth
        });

        await Assert.That(VictimMaxHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    private static Unit CreateCaster() => new TestCaster
    {
        ObjId = 100,
        Level = 50,
        Hp = CasterMaxHp,
        MaxHp = CasterMaxHp,
        Mp = 1_000,
        MaxMp = 1_000,
        LevelDps = LevelDpsForExactDamage
    };

    private static TestVictim CreateVictim(int maxHp = VictimMaxHp) => new()
    {
        ObjId = 200,
        Level = 50,
        Hp = maxHp,
        MaxHp = maxHp,
        Mp = 1_000,
        MaxMp = 1_000
    };

    private static void Hit(Unit caster, Unit victim, DamageEffect effect)
    {
        effect.Id = 1;
        effect.DamageType = DamageType.Melee;
        effect.Multiplier = effect.Multiplier == 0f ? 1f : effect.Multiplier;
        effect.DpsIncMultiplier = 1f;
        // Level damage with no level variance and no weapon slot: the composition is exactly the 100 of
        // CreateCaster, so the roll cannot pick a different number.
        effect.UseLevelDamage = true;
        effect.LevelMd = LevelMdForExactDamage;
        effect.WeaponSlotId = -1;

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

    /// <summary>A caster whose caster-side packets are dropped rather than broadcast.</summary>
    private class TestCaster : Unit
    {
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }

    /// <summary>A victim with no armour, so the hit is not scaled by the 5300 curve.</summary>
    private sealed class TestVictim : TestCaster
    {
        public override int Armor => 0;
        public override int MagicResistance => 0;
    }

    /// <summary>
    /// The aggro path asks <c>SkillManager</c> for the buffs of the NoFight/Returning tags; an empty tag
    /// table answers "no such tag" and the previous singleton is put back afterwards.
    /// </summary>
    private sealed class EmptySkillManagerScope : IDisposable
    {
        private static readonly FieldInfo InstanceField =
            typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

        private readonly object _previous;

        public EmptySkillManagerScope()
        {
            var skillManager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
            SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>());
            _previous = InstanceField.GetValue(null);
            InstanceField.SetValue(null, skillManager);
        }

        public void Dispose() => InstanceField.SetValue(null, _previous);

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null)
                    continue;
                field.SetValue(target, value);
                return;
            }

            throw new InvalidOperationException($"Missing field {name}");
        }
    }
}
