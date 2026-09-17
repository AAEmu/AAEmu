using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <c>mana_damage</c> (6 rows), <c>cancel_protection</c> (78 rows clear it), and the two mana pools
/// <c>percent_damage_resource_type_id</c> can name, all through the real <see cref="DamageEffect"/> path.
/// </summary>
[NotInParallel]
public class ManaDamageAndProtectionTests
{
    private const int ComposedDamage = 100;
    private const float LevelDpsForExactDamage = 199f;
    private const float LevelMdForExactDamage = 0.5f;
    private const int StartHp = 20_000;
    private const int StartMp = 5_000;

    // Synthetic buff ids: only the column names below come from the DB.
    private const uint MeleeImmuneBuffId = 91030;
    private const uint PlainBuffId = 91031;
    private const uint SpellImmuneBuffId = 91032;

    private FieldInfo _skillManagerField;
    private FieldInfo _buffGameDataField;
    private FieldInfo _effectTaskManagerField;
    private FieldInfo _taskManagerField;
    private object _previousSkillManager;
    private object _previousBuffGameData;
    private object _previousEffectTaskManager;
    private object _previousTaskManager;

    [Before(Test)]
    public void InstallContentLookups()
    {
        _skillManagerField = SingletonField<SkillManager>();
        _buffGameDataField = SingletonField<BuffGameData>();
        _effectTaskManagerField = SingletonField<EffectTaskManager>();
        _taskManagerField = SingletonField<TaskManager>();
        _previousSkillManager = _skillManagerField.GetValue(null);
        _previousBuffGameData = _buffGameDataField.GetValue(null);
        _previousEffectTaskManager = _effectTaskManagerField.GetValue(null);
        _previousTaskManager = _taskManagerField.GetValue(null);

        var skillManager = new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object);
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(skillManager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [MeleeImmuneBuffId] = new() { Id = MeleeImmuneBuffId, Duration = 10_000, MeleeImmune = true },
            [SpellImmuneBuffId] = new() { Id = SpellImmuneBuffId, Duration = 10_000, SpellImmune = true },
            [PlainBuffId] = new() { Id = PlainBuffId, Duration = 10_000 }
        });
        _skillManagerField.SetValue(null, skillManager);

        var buffGameData = new BuffGameData();
        SetField(buffGameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(buffGameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(buffGameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        _buffGameDataField.SetValue(null, buffGameData);

        var taskManager = new TaskManager(Mock.Of<ITickManager>().Object);
        _taskManagerField.SetValue(null, taskManager);
        _effectTaskManagerField.SetValue(null, new EffectTaskManager(taskManager));
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        _skillManagerField.SetValue(null, _previousSkillManager);
        _buffGameDataField.SetValue(null, _previousBuffGameData);
        _effectTaskManagerField.SetValue(null, _previousEffectTaskManager);
        _taskManagerField.SetValue(null, _previousTaskManager);
    }

    // ---------------------------------------------------------------------------------------------------
    // mana_damage
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task ManaDamage_DrainsMana_AndLeavesHealthAlone()
    {
        // Skill 38807 활력 흡수, the only skill behind these six rows.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect { ManaDamage = true });

        await Assert.That(StartMp - victim.Mp).IsEqualTo(ComposedDamage);
        await Assert.That(victim.Hp).IsEqualTo(StartHp);
    }

    [Test]
    public async Task WithoutTheFlag_HealthIsHitAndManaIsNot()
    {
        // The 10,995 rows that do not set mana_damage.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect());

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
        await Assert.That(victim.Mp).IsEqualTo(StartMp);
    }

    [Test]
    public async Task ManaDamage_WithAPercentOfTheVictimsMana_AddsTheShareToTheDrain()
    {
        // percent_damage_resource_type_id 3 (current_mana) / 4 (max_mana) are the two mana rows of
        // enum_percent_damage_resource_types; here 10 % of the victim's 5,000 current mana rides along.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect
        {
            ManaDamage = true,
            UsePercentDamage = true,
            PercentMin = 10,
            PercentMax = 10,
            PercentDamageResourceTypeId = (int)PercentDamageResourceType.CurrentMana
        });

        await Assert.That(StartMp - victim.Mp).IsEqualTo(ComposedDamage + 500);
        await Assert.That(victim.Hp).IsEqualTo(StartHp);
    }

    [Test]
    public async Task ManaDamage_OnAQuarterFullPool_TakesWhatIsThere()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();
        victim.Mp = 40;

        Hit(caster, victim, new DamageEffect { ManaDamage = true });

        await Assert.That(victim.Mp).IsEqualTo(0);
    }

    // ---------------------------------------------------------------------------------------------------
    // cancel_protection
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task AnImmuneVictim_IsProtectedByDefault()
    {
        // cancel_protection is 't' on 10,923 of the 11,001 rows, and that is the behaviour DamageEffect had
        // before this change: the immunity check runs and the hit deals nothing.
        var caster = CreateCaster();
        var victim = CreateVictim();
        AddBuff(victim, caster, MeleeImmuneBuffId);

        Hit(caster, victim, new DamageEffect { CancelProtection = true });

        await Assert.That(victim.Hp).IsEqualTo(StartHp);
    }

    [Test]
    public async Task CancelProtectionCleared_LandsThroughTheImmunity()
    {
        // The 78 rows that clear it: 신 오스트 투석기 발사, 홍염포 발사, 차원 격벽 파괴 and the other
        // mechanical hits that have to reach a protected victim.
        var caster = CreateCaster();
        var victim = CreateVictim();
        AddBuff(victim, caster, MeleeImmuneBuffId);

        Hit(caster, victim, new DamageEffect { CancelProtection = false });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task ANonImmuneBuff_MakesNoDifferenceEitherWay()
    {
        var caster = CreateCaster();

        var protectedVictim = CreateVictim();
        AddBuff(protectedVictim, caster, PlainBuffId);
        Hit(caster, protectedVictim, new DamageEffect { CancelProtection = true });

        var openVictim = CreateVictim();
        AddBuff(openVictim, caster, PlainBuffId);
        Hit(caster, openVictim, new DamageEffect { CancelProtection = false });

        await Assert.That(StartHp - protectedVictim.Hp).IsEqualTo(ComposedDamage);
        await Assert.That(StartHp - openVictim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task AnImmunityOfAnotherDamageType_DoesNotStopAMeleeHit()
    {
        // The immunity template is spell-only, and CheckDamageImmune is asked about the hit's own type.
        var caster = CreateCaster();
        var victim = CreateVictim();
        AddBuff(victim, caster, SpellImmuneBuffId);

        Hit(caster, victim, new DamageEffect { CancelProtection = true });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task TheSameImmunity_StopsTheMatchingHitType()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();
        AddBuff(victim, caster, SpellImmuneBuffId);

        Hit(caster, victim, new DamageEffect { CancelProtection = true }, DamageType.Magic);

        await Assert.That(victim.Hp).IsEqualTo(StartHp);
    }

    private static TestUnit CreateCaster() => new()
    {
        ObjId = 100,
        Level = 50,
        Hp = StartHp,
        MaxHp = StartHp,
        Mp = StartMp,
        MaxMp = StartMp,
        LevelDps = LevelDpsForExactDamage
    };

    private static TestUnit CreateVictim() => new()
    {
        ObjId = 200,
        Level = 50,
        Hp = StartHp,
        MaxHp = StartHp,
        Mp = StartMp,
        MaxMp = StartMp
    };

    private static void AddBuff(Unit owner, Unit caster, uint buffId)
    {
        owner.Buffs.AddBuff(new Buff(owner, caster, new SkillCasterUnit(caster.ObjId),
            SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow)
        {
            Passive = true,
            AbLevel = 1
        });
    }

    private static void Hit(Unit caster, Unit victim, DamageEffect effect, DamageType damageType = DamageType.Melee)
    {
        effect.Id = 1;
        effect.DamageType = damageType;
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

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
