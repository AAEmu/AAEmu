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
/// The weapon flags and <c>use_combat_resource</c> through the real <see cref="DamageEffect"/> path.
/// </summary>
/// <remarks>
/// The weapon flags are covered on the side a unit test can reach: a caster with no weapon item, where the
/// composed Dps attribute is the input and the hit is exactly what it was. The weapon's own value winning is
/// <c>DamageEffectRulesTests</c>' case, because a real <c>Weapon.Dps</c> needs item grade and formula data the
/// content tables would have to supply.
/// </remarks>
[NotInParallel]
public class CombatResourceAndWeaponTests
{
    private const int ComposedDamage = 100;
    private const float LevelDpsForExactDamage = 199f;
    private const float LevelMdForExactDamage = 0.5f;
    private const int StartHp = 40_000;
    private const int AdamantPoolId = 3; // combat_resources 3, 근성

    private EmptySkillManagerScope _skillManagerScope;

    [Before(Test)]
    public void InstallEmptySkillManager() => _skillManagerScope = new EmptySkillManagerScope();

    [After(Test)]
    public void RestoreSkillManager() => _skillManagerScope?.Dispose();

    [Test]
    public async Task AWeaponFlaggedRow_WithoutAWeapon_StillReadsTheComposedAttribute()
    {
        // Every NPC, and any caster whose slot is empty: 5,000 on the attribute is 5 damage, exactly as the
        // flag behaved before.
        var caster = CreateCaster();
        caster.Dps = 5_000;
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect { UseMainhandWeapon = true });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage + 5);
    }

    [Test]
    public async Task AWeaponFlaggedRow_WithNoAttributeAtAll_IsTheComposedHit()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect { UseMainhandWeapon = true });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task WithoutTheCombatResourceFlag_TheHitIsUnchanged()
    {
        // The 10,985 rows that do not set it.
        var caster = CreateCaster();
        caster.CombatResources[AdamantPoolId] = 200;
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect { CombatResourceMd = 12.5f });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task ACombatResourceRow_AddsTheShareOfThePool()
    {
        // 승자의 외침: 불꽃 (damage effect 13204): skills.combat_resource_id 3, combat_resource_md 1.0, so
        // 200 stacked 근성 adds 200 to the composed 100.
        var caster = CreateCaster();
        caster.CombatResources[AdamantPoolId] = 200;
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            UseCombatResource = true,
            CombatResourceMd = 1f,
            CombatResourceLevelMd = 0f,
            CombatResourceDpsMd = 0f
        }, skillResourceId: AdamantPoolId);

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage + 200);
    }

    [Test]
    public async Task ACombatResourceRow_ScalesWithTheAuthoredMultiplier()
    {
        // The 12.5 twin (damage effect 13206): 1,250 % of the same 200 is 2,500.
        var caster = CreateCaster();
        caster.CombatResources[AdamantPoolId] = 200;
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            UseCombatResource = true,
            CombatResourceMd = 12.5f,
            CombatResourceLevelMd = 0f,
            CombatResourceDpsMd = 0f
        }, skillResourceId: AdamantPoolId);

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage + 2_500);
    }

    [Test]
    public async Task ACombatResourceRow_WithAnEmptyPool_AddsNothing()
    {
        // The flags are set but the caster holds none of the pool the skill declares.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            UseCombatResource = true,
            CombatResourceMd = 12.5f,
            CombatResourceLevelMd = 0f,
            CombatResourceDpsMd = 0f
        }, skillResourceId: AdamantPoolId);

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task ACombatResourceRow_WithoutASkillPool_AddsNothing()
    {
        // A skill with no combat_resource_id (38,031 of the 38,043 rows): there is no pool to read.
        var caster = CreateCaster();
        caster.CombatResources[AdamantPoolId] = 200;
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            UseCombatResource = true,
            CombatResourceMd = 12.5f,
            CombatResourceLevelMd = 0f,
            CombatResourceDpsMd = 0f
        });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    private static TestUnit CreateCaster() => new()
    {
        ObjId = 100,
        Level = 50,
        Hp = StartHp,
        MaxHp = StartHp,
        LevelDps = LevelDpsForExactDamage
    };

    private static TestUnit CreateVictim() => new()
    {
        ObjId = 200,
        Level = 50,
        Hp = StartHp,
        MaxHp = StartHp
    };

    private static void Hit(Unit caster, Unit victim, DamageEffect effect, int skillResourceId = 0)
    {
        effect.Id = 1;
        effect.DamageType = DamageType.Melee;
        effect.Multiplier = 1f;
        effect.DpsIncMultiplier = 1f;
        // dps_multiplier is 1.0 on 9,362 of the 11,001 rows; without it the weapon term is scaled to nothing.
        effect.DpsMultiplier = 1f;
        effect.UseLevelDamage = true;
        effect.LevelMd = LevelMdForExactDamage;
        effect.WeaponSlotId = -1;

        var skill = new Skill(new SkillTemplate { Id = 1, CombatResourceId = skillResourceId }) { Level = 1 };

        effect.Apply(
            caster,
            new SkillCasterUnit(caster.ObjId),
            victim,
            new SkillCastUnitTarget(victim.ObjId),
            new CastSkill(1, 1),
            new EffectSource(skill),
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
