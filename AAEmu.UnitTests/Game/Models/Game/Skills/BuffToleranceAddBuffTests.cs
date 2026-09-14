using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The reported crash, at the level it was reported: a tolerance-tagged CC applied while its family's
/// final-step immunity buff was already up threw <c>ArgumentException</c> ("An item with the same key has
/// already been added. Key: 4") out of <c>Buffs.AddBuff</c>. The throw escaped the effect loop in
/// <c>Skill.ApplyEffects</c>, so the rest of the cast's effects and its <c>EndSkill</c> never ran.
/// </summary>
/// <remarks>
/// content: buff_tolerances 4 (buff_tag_id 6, step_duration 30, final_step_buff_id 2371) with
/// buff_tolerance_steps 0 %, 25 %, 50 %, 0 % — the ids are faked here, the shape is the live one.
/// </remarks>
[NotInParallel]
public class BuffToleranceAddBuffTests
{
    private const uint CcBuffId = 91001;
    private const uint SiblingCcBuffId = 91003;
    private const uint ImmunityBuffId = 91002;
    private const uint ToleranceId = 4;
    private const uint ToleranceTagId = 6;

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
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>
        {
            [CcBuffId] = [ToleranceTagId],
            [SiblingCcBuffId] = [ToleranceTagId]
        });
        SetField(skillManager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [CcBuffId] = new BuffTemplate { Id = CcBuffId, Duration = 5000 },
            [SiblingCcBuffId] = new BuffTemplate { Id = SiblingCcBuffId, Duration = 5000 },
            [ImmunityBuffId] = new BuffTemplate { Id = ImmunityBuffId, Duration = 0 }
        });
        _skillManagerField.SetValue(null, skillManager);

        var tolerance = CreateTolerance();
        var buffGameData = new BuffGameData();
        SetField(buffGameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(buffGameData, "_buffTolerances", new Dictionary<uint, BuffTolerance> { [ToleranceTagId] = tolerance });
        SetField(buffGameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance> { [ToleranceId] = tolerance });
        _buffGameDataField.SetValue(null, buffGameData);

        // A timed CC schedules its own expiry and a refresh clears the pending one; neither should
        // reach a real scheduler.
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

    [Test]
    public async Task AddBuff_WhileTheImmunityBuffIsUp_DropsTheCcInsteadOfThrowing()
    {
        var (owner, caster) = CreateUnits();
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId));
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, ImmunityBuffId));
        await Assert.That(owner.Buffs.CheckBuff(ImmunityBuffId)).IsTrue();
        owner.Buffs.RemoveBuff(CcBuffId, notifyZone: false);

        // Second application of the same family while the immunity is up: this is the call that threw.
        Exception thrown = null;
        try
        {
            owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId));
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNull();
        await Assert.That(owner.Buffs.CheckBuff(CcBuffId)).IsFalse();
    }

    [Test]
    public async Task AddBuff_WhileTheImmunityBuffIsUp_LeavesTheCounterWhereItWas()
    {
        var (owner, caster) = CreateUnits();
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId)); // opens the ladder at 0 %
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId)); // steps to 25 %
        var beforeImmunity = ReadCounter(owner).CurrentStep;
        await Assert.That(beforeImmunity.TimeReduction).IsEqualTo(25u);

        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, ImmunityBuffId));
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId));

        // An immune CC must not move the ladder: the counter is what decides when the next immunity is
        // granted, so advancing it here would shorten the ladder for the burst after the immunity ends.
        var afterImmunity = ReadCounter(owner);
        await Assert.That(afterImmunity.CurrentStep).IsSameReferenceAs(beforeImmunity);
    }

    [Test]
    public async Task AddBuff_AnotherMemberOfTheFamilyWhileImmune_DoesNotLand()
    {
        var (owner, caster) = CreateUnits();
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId));
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, ImmunityBuffId));

        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, SiblingCcBuffId));

        await Assert.That(owner.Buffs.CheckBuff(SiblingCcBuffId)).IsFalse();
        await Assert.That(owner.Buffs.CheckBuff(CcBuffId)).IsTrue();
    }

    [Test]
    public async Task AddBuff_ThroughTheWholeLadder_HandsOutTheFinalStepImmunity()
    {
        var (owner, caster) = CreateUnits();

        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId)); // 0 %
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId)); // 25 %
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId)); // 50 %
        await Assert.That(owner.Buffs.CheckBuff(ImmunityBuffId)).IsFalse();

        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId)); // trailing 0 %: the ladder is over

        await Assert.That(owner.Buffs.CheckBuff(ImmunityBuffId)).IsTrue();
        var counter = ReadCounter(owner);
        await Assert.That(counter.CurrentStep.TimeReduction).IsEqualTo(0u);
        await Assert.That(counter.CurrentStep).IsSameReferenceAs(counter.Tolerance.Steps[0]);
    }

    [Test]
    public async Task AddBuff_AfterTheStepWindow_LetsTheCcThroughAgain()
    {
        var (owner, caster) = CreateUnits();
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId));
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId));
        await Assert.That(ReadCounter(owner).CurrentStep.TimeReduction).IsEqualTo(25u);

        // 31 s later the burst is over, so the ladder starts again rather than reaching for the immunity.
        ReadCounter(owner).LastStep = DateTime.UtcNow.AddSeconds(-31);
        owner.Buffs.AddBuff(CreateCcBuff(owner, caster, CcBuffId));

        await Assert.That(ReadCounter(owner).CurrentStep.TimeReduction).IsEqualTo(0u);
        await Assert.That(owner.Buffs.CheckBuff(ImmunityBuffId)).IsFalse();
    }

    private static (BaseUnit Owner, BaseUnit Caster) CreateUnits() =>
        (new BaseUnit { ObjId = 1 }, new BaseUnit { ObjId = 2 });

    private static Buff CreateCcBuff(BaseUnit owner, BaseUnit caster, uint buffId) =>
        new(owner, caster, new SkillCasterUnit(caster.ObjId),
            SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow)
        {
            // Keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test with no connection.
            Passive = true,
            AbLevel = 1
        };

    private static BuffToleranceCounter ReadCounter(BaseUnit owner)
    {
        var counters = (Dictionary<uint, BuffToleranceCounter>)typeof(Buffs)
            .GetField("_toleranceCounters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner.Buffs);
        return counters[ToleranceId];
    }

    private static BuffTolerance CreateTolerance()
    {
        var tolerance = new BuffTolerance
        {
            Id = ToleranceId,
            BuffTagId = ToleranceTagId,
            StepDuration = 30,
            FinalStepBuffId = ImmunityBuffId,
            CharacterTimeReduction = 0,
            Steps = []
        };

        var stepId = 6u;
        foreach (var timeReduction in new uint[] { 0, 25, 50, 0 })
        {
            tolerance.Steps.Add(new BuffToleranceStep
            {
                Id = stepId++,
                BuffTolerance = tolerance,
                HitChance = 100,
                TimeReduction = timeReduction
            });
        }

        return tolerance;
    }

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
