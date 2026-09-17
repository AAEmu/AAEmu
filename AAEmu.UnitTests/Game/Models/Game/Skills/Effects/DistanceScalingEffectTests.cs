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
/// The two distance flags through the real <see cref="DamageEffect"/> path, at the one thing a unit test can
/// pin without the content DB: a process with no <c>formulas</c> table loaded. Both flags then have to be
/// exactly neutral, because that is also what an absent row 11 or 12 means in production.
/// </summary>
/// <remarks>
/// The arithmetic of the two rows themselves is in <see cref="FormulaDamageScalingRulesTests"/>, which
/// evaluates the shipped expressions directly.
/// </remarks>
[NotInParallel]
public class DistanceScalingEffectTests
{
    private const int ComposedDamage = 100;
    private const float LevelDpsForExactDamage = 199f;
    private const float LevelMdForExactDamage = 0.5f;
    private const int VictimStartHp = 20_000;

    private EmptySkillManagerScope _skillManagerScope;

    [Before(Test)]
    public void InstallEmptySkillManager() => _skillManagerScope = new EmptySkillManagerScope();

    [After(Test)]
    public void RestoreSkillManager() => _skillManagerScope?.Dispose();

    [Test]
    public async Task HeightFlagWithNoFormulaRow_LeavesTheHitExactlyWhereItWas()
    {
        // adjust_damage_by_height is set on 10,584 rows, so this is the flag that would move the most damage
        // if a missing formulas table were read as anything other than "no scale".
        var caster = CreateCaster(x: 0f, y: 0f, z: 40f);
        var victim = CreateVictim(x: 0f, y: 0f, z: 0f);

        Hit(caster, victim, new DamageEffect { AdjustDamageByHeight = true });

        await Assert.That(VictimStartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task RangeFlagWithNoFormulaRow_LeavesTheHitExactlyWhereItWas()
    {
        // The 123 adjust_damage_by_range rows, at their own shipped optimum_range/range_damage_multipier.
        var caster = CreateCaster(x: 0f, y: 0f, z: 0f);
        var victim = CreateVictim(x: 30f, y: 0f, z: 0f);

        Hit(caster, victim, new DamageEffect
        {
            AdjustDamageByRange = true,
            OptimumRange = 30f,
            RangeDamageMultiplier = 1.3f
        });

        await Assert.That(VictimStartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task BothFlagsOff_IsTheSameHit()
    {
        // The 417 rows that opt out of the height term and the 10,878 that never set the range one.
        var caster = CreateCaster(x: 0f, y: 0f, z: 40f);
        var victim = CreateVictim(x: 25f, y: 0f, z: 0f);

        Hit(caster, victim, new DamageEffect { OptimumRange = 25f, RangeDamageMultiplier = 2f });

        await Assert.That(VictimStartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    private static TestUnit CreateCaster(float x, float y, float z)
    {
        var caster = new TestUnit
        {
            ObjId = 100,
            Level = 50,
            Hp = VictimStartHp,
            MaxHp = VictimStartHp,
            LevelDps = LevelDpsForExactDamage
        };
        caster.Transform.Local.SetPosition(x, y, z);
        return caster;
    }

    private static TestUnit CreateVictim(float x, float y, float z)
    {
        var victim = new TestUnit
        {
            ObjId = 200,
            Level = 50,
            Hp = VictimStartHp,
            MaxHp = VictimStartHp
        };
        victim.Transform.Local.SetPosition(x, y, z);
        return victim;
    }

    private static void Hit(Unit caster, Unit victim, DamageEffect effect)
    {
        effect.Id = 1;
        effect.DamageType = DamageType.Melee;
        effect.Multiplier = 1f;
        effect.DpsIncMultiplier = 1f;
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

    /// <summary>A unit with no armour and no world to broadcast into.</summary>
    private sealed class TestUnit : Unit
    {
        public override int Armor => 0;
        public override int MagicResistance => 0;
        public override void BroadcastPacket(GamePacket packet, bool self) { }
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
