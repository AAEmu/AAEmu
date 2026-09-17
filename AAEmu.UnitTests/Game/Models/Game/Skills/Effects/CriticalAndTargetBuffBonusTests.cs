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
/// <c>critical_bonus</c> and <c>target_buff_bonus</c> through the real <see cref="DamageEffect"/> path.
/// </summary>
/// <remarks>
/// The critical case is forced, not rolled: <c>MeleeCritical</c> at 100 makes the hit type test
/// (<c>Random.Shared.Next(0f, 100f) &lt; MeleeCritical - flexibility</c>) true on every draw, so the crit
/// branch is the only thing under test.
/// </remarks>
[NotInParallel]
public class CriticalAndTargetBuffBonusTests
{
    private const int ComposedDamage = 100;
    private const float LevelDpsForExactDamage = 199f;
    private const float LevelMdForExactDamage = 0.5f;
    private const int StartHp = 20_000;

    // Synthetic ids: only the column names come from the DB.
    private const uint TaggedBuffId = 91040;
    private const uint PlainBuffId = 91041;
    private const uint TagId = 91340;

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
            [TaggedBuffId] = new() { Id = TaggedBuffId, Duration = 10_000 },
            [PlainBuffId] = new() { Id = PlainBuffId, Duration = 10_000 }
        });
        // CheckBuffTag asks for the buffs carrying a tag, not the tags of a buff.
        SetField(skillManager, "_taggedBuffs", new Dictionary<uint, List<uint>> { [TagId] = [TaggedBuffId] });
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
    // critical_bonus
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task ACritWithoutAnEffectBonus_DealsWhatItAlwaysDid()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect());

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task ACritWithAnEffectBonus_DealsThatMuchMore()
    {
        // damage_effects 2225/2227 author critical_bonus 100 on a caster carrying none: the crit factor goes
        // from 1.0 to 2.0.
        var caster = CreateCaster();
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect { CriticalBonus = 100 });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage * 2);
    }

    [Test]
    public async Task ACritWithBothBonuses_AddsThem()
    {
        // The caster's own 50 % plus the effect's 100 is 150 %, so the hit is 2.5×.
        var caster = CreateCaster();
        caster.MeleeCriticalBonus = 50f;
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect { CriticalBonus = 100 });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage * 5 / 2);
    }

    [Test]
    public async Task ANonCriticalHit_IgnoresTheEffectBonus()
    {
        // The bonus is the critical path's own: a plain hit is the composed damage either way.
        var caster = CreateCaster(critical: false);
        var victim = CreateVictim();

        Hit(caster, victim, new DamageEffect { CriticalBonus = 100 });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    // ---------------------------------------------------------------------------------------------------
    // target_buff_bonus
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task ATaggedVictim_TakesTheFlatAdd()
    {
        // The shape of the 광선포/화염 방사기 rows: a tagged victim takes the authored add on top.
        var caster = CreateCaster();
        var victim = CreateVictim();
        AddBuff(victim, caster, TaggedBuffId);

        Hit(caster, victim, new DamageEffect
        {
            TargetBuffTagId = TagId,
            TargetBuffBonus = 5_000,
            TargetBuffBonusMul = 1f
        });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage + 5_000);
    }

    [Test]
    public async Task ATaggedVictim_TakesTheMultiplierThenTheAdd()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();
        AddBuff(victim, caster, TaggedBuffId);

        Hit(caster, victim, new DamageEffect
        {
            TargetBuffTagId = TagId,
            TargetBuffBonus = 10_000,
            TargetBuffBonusMul = 2f
        });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage * 2 + 10_000);
    }

    [Test]
    public async Task AnUntaggedVictim_IsUntouchedByThePair()
    {
        // 10,980 rows author no add, and a victim without the tag never reaches the pair at all.
        var caster = CreateCaster();
        var victim = CreateVictim();
        AddBuff(victim, caster, PlainBuffId);

        Hit(caster, victim, new DamageEffect
        {
            TargetBuffTagId = TagId,
            TargetBuffBonus = 5_000,
            TargetBuffBonusMul = 2f
        });

        await Assert.That(StartHp - victim.Hp).IsEqualTo(ComposedDamage);
    }

    private static TestUnit CreateCaster(bool critical = true) => new()
    {
        ObjId = 100,
        Level = 50,
        Hp = StartHp,
        MaxHp = StartHp,
        LevelDps = LevelDpsForExactDamage,
        // 100 % melee critical chance, and no flexibility on the victim to subtract from it.
        MeleeCritical = critical ? 100f : 0f
    };

    private static TestUnit CreateVictim() => new()
    {
        ObjId = 200,
        Level = 50,
        Hp = StartHp,
        MaxHp = StartHp
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

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
