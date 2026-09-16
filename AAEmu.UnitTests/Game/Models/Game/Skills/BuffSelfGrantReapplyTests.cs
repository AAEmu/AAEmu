using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// What happens to the live instance when a buff whose own <c>tagged_immune_buffs</c> row names a tag it
/// carries is applied a second time. The immunity check runs before <c>Buffs.AddBuff</c> reaches its stack
/// rule, so the self-grant has to step aside exactly where the re-application has an outcome: 93 동결
/// refreshes its six seconds, 631 수감자 extends its 1 800 000 ms, and a 60-second 쿨타임 체크용 marker is
/// refused rather than replaced, because replacing it is what restarts the cooldown it holds.
/// </summary>
/// <remarks>
/// The stack rules, durations, ceilings and tags are the live ones; only the content rows are faked. The
/// lookups are the real <see cref="SkillManager"/>, <see cref="BuffGameData"/>, <see cref="TaskManager"/> and
/// <see cref="EffectTaskManager"/> singletons with their tables installed directly, and the candidates go
/// through <see cref="BuffEffect.Apply"/> so the immunity gate is the one the game runs.
/// </remarks>
[NotInParallel]
public class BuffSelfGrantReapplyTests
{
    private const uint FreezeBuffId = 93; // 동결: stack_rule_id 1 refresh, max_stack 10, 6000 ms
    private const uint FreezeTagId = 919; // 차가운 발걸음, carried by 93
    private const uint PrisonerBuffId = 631; // 수감자: stack_rule_id 5 extend, max_stack 1, 1 800 000 ms
    private const uint PrisonerTagId = 344; // 수감자, carried by 631/2028/3623/4868/8038
    private const uint CooldownMarkerBuffId = 25466; // 쿨타임 체크용: stack_rule_id 6 independent, max_stack 1, 60 000 ms
    private const uint CooldownTagId = 4447; // carried by 25466 alone
    private const uint TaggedCandidateId = 91001; // a second buff carrying 4447
    private const uint OwnerObjId = 1u;
    private const uint CasterObjId = 2u;

    private const int FreezeDurationMs = 6000;
    private const int PrisonerDurationMs = 1800000;
    private const int CooldownMarkerDurationMs = 60000;

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
        SetField(skillManager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [FreezeBuffId] = new()
            {
                Id = FreezeBuffId,
                Duration = FreezeDurationMs,
                StackRule = BuffStackRule.Refresh,
                MaxStack = 10
            },
            [PrisonerBuffId] = new()
            {
                Id = PrisonerBuffId,
                Duration = PrisonerDurationMs,
                StackRule = BuffStackRule.Extend,
                MaxStack = 1
            },
            [CooldownMarkerBuffId] = new()
            {
                Id = CooldownMarkerBuffId,
                Duration = CooldownMarkerDurationMs,
                StackRule = BuffStackRule.Independent,
                MaxStack = 1
            },
            [TaggedCandidateId] = new()
            {
                Id = TaggedCandidateId,
                Duration = 10000,
                StackRule = BuffStackRule.Refresh,
                MaxStack = 1
            }
        });
        // tagged_buffs, both directions, exactly as SkillManager.Load fills them.
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>
        {
            [FreezeBuffId] = [FreezeTagId],
            [PrisonerBuffId] = [PrisonerTagId],
            [CooldownMarkerBuffId] = [CooldownTagId],
            [TaggedCandidateId] = [CooldownTagId]
        });
        SetField(skillManager, "_taggedBuffs", new Dictionary<uint, List<uint>>
        {
            [FreezeTagId] = [FreezeBuffId],
            [PrisonerTagId] = [PrisonerBuffId],
            [CooldownTagId] = [CooldownMarkerBuffId, TaggedCandidateId]
        });
        // tagged_immune_buffs: while the key buff is up, a candidate carrying the value tag is refused.
        // Each of the three names a tag its own buff carries.
        SetField(skillManager, "_buffImmunityTags", new Dictionary<uint, List<uint>>
        {
            [FreezeBuffId] = [FreezeTagId],
            [PrisonerBuffId] = [PrisonerTagId],
            [CooldownMarkerBuffId] = [CooldownTagId]
        });
        SetField(skillManager, "_requiredBuffTags", new Dictionary<uint, List<uint>>());
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

    [Test]
    public async Task Reapply_RefreshSelfGrantingRow_RefreshesTheLiveInstance()
    {
        // 93 동결: the second freeze restarts the six seconds rather than being refused by tag 919, which 93
        // carries itself.
        var (owner, caster) = CreateUnits();
        var live = AddDirectly(owner, caster, FreezeBuffId);
        AgeBy(live, FreezeDurationMs - 1000); // one second left

        ApplyThroughEffectPath(caster, owner, FreezeBuffId);

        await Assert.That(owner.Buffs.GetBuffCountById(FreezeBuffId)).IsEqualTo(1);
        await Assert.That(owner.Buffs.GetEffectFromBuffId(FreezeBuffId)).IsSameReferenceAs(live);
        await Assert.That(live.GetTimeLeft()).IsGreaterThan(FreezeDurationMs - 1500);
    }

    [Test]
    public async Task Reapply_ExtendSelfGrantingRow_AddsTheNewDurationToWhatIsLeft()
    {
        // 631 수감자: the row is immune to the tag 631 carries, but an extend row must reach
        // Buff.OverwriteWith, which sets the new duration to the remaining time plus the incoming one.
        var (owner, caster) = CreateUnits();
        var live = AddDirectly(owner, caster, PrisonerBuffId);
        const int elapsed = 600000; // ten of the thirty minutes gone
        AgeBy(live, elapsed);

        ApplyThroughEffectPath(caster, owner, PrisonerBuffId);

        await Assert.That(owner.Buffs.GetBuffCountById(PrisonerBuffId)).IsEqualTo(1);
        await Assert.That(owner.Buffs.GetEffectFromBuffId(PrisonerBuffId)).IsSameReferenceAs(live);
        // What was left of the first application plus another 1 800 000 ms, not a restart at 1 800 000.
        var expected = PrisonerDurationMs + (PrisonerDurationMs - elapsed);
        await Assert.That(live.Duration).IsGreaterThan(expected - 5000);
        await Assert.That(live.Duration).IsLessThan(expected + 5000);
        await Assert.That(live.GetTimeLeft()).IsGreaterThan(PrisonerDurationMs);
    }

    [Test]
    public async Task Reapply_IndependentSelfGrantingRow_IsRefusedAndTheMarkerKeepsItsTimer()
    {
        // 25466 (and the eleven other 60-second 쿨타임 체크용 markers): tag 4447 is carried by 25466 alone, so
        // the row's whole effect is to refuse the re-application. Landing it would replace the live instance
        // and restart the cooldown, which is what the marker exists to hold.
        var (owner, caster) = CreateUnits();
        var live = AddDirectly(owner, caster, CooldownMarkerBuffId);
        AgeBy(live, CooldownMarkerDurationMs - 30000); // thirty seconds left of the minute
        var before = live.GetTimeLeft();

        await Assert.That(owner.Buffs.CheckBuffImmune(SkillManager.Instance.GetBuffTemplate(CooldownMarkerBuffId),
            caster)).IsTrue();

        ApplyThroughEffectPath(caster, owner, CooldownMarkerBuffId);

        await Assert.That(owner.Buffs.GetBuffCountById(CooldownMarkerBuffId)).IsEqualTo(1);
        await Assert.That(owner.Buffs.GetEffectFromBuffId(CooldownMarkerBuffId)).IsSameReferenceAs(live);
        await Assert.That(live.GetTimeLeft()).IsLessThanOrEqualTo(before);
        await Assert.That(live.GetTimeLeft()).IsLessThan(CooldownMarkerDurationMs - 15000);
    }

    [Test]
    public async Task Reapply_AnotherBuffCarryingTheMarkerTag_IsStillRefused()
    {
        // The scoped self-skip is about the marker's own id: another buff carrying tag 4447 is refused
        // exactly as it was before, so the immunity a marker grants is not weakened by the fix.
        var (owner, caster) = CreateUnits();
        AddDirectly(owner, caster, CooldownMarkerBuffId);

        var refused = owner.Buffs.CheckBuffImmune(SkillManager.Instance.GetBuffTemplate(TaggedCandidateId),
            caster);

        await Assert.That(refused).IsTrue();

        ApplyThroughEffectPath(caster, owner, TaggedCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(TaggedCandidateId)).IsFalse();
    }

    private static (BaseUnit Owner, Unit Caster) CreateUnits() =>
        (new BaseUnit { ObjId = OwnerObjId }, new Unit { ObjId = CasterObjId });

    /// <summary>
    /// Lands a buff the way an initial application does, quiet on the wire, and returns the live instance.
    /// </summary>
    private static Buff AddDirectly(BaseUnit owner, Unit caster, uint buffId)
    {
        var template = SkillManager.Instance.GetBuffTemplate(buffId);

        owner.Buffs.AddBuff(new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), template, null,
            DateTime.UtcNow)
        {
            // Keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test with no connection.
            Passive = true,
            AbLevel = 1
        });

        return owner.Buffs.GetEffectFromBuffId(buffId);
    }

    /// <summary>Walks the instance's start time back, i.e. lets <paramref name="milliseconds"/> pass.</summary>
    private static void AgeBy(Buff buff, int milliseconds) =>
        buff.StartTime = buff.StartTime.AddMilliseconds(-milliseconds);

    private static void ApplyThroughEffectPath(Unit caster, BaseUnit owner, uint buffId)
    {
        var template = SkillManager.Instance.GetBuffTemplate(buffId);
        var effect = new BuffEffect { Chance = 100, Buff = template };
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), owner, new SkillCastUnitTarget(owner.ObjId),
            new CastSkill(0, 1), new EffectSource(), new SkillObject(), DateTime.UtcNow);
    }

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
