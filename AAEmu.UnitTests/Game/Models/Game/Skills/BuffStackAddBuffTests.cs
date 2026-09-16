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
/// Each stack rule at the level it lands: <c>Buffs.AddBuff</c>. The pure ceilings live in
/// <see cref="BuffStackRulesTests"/>; what is asserted here is how many instances a family ends up
/// with, what one instance represents, and what a second application does to the first.
/// </summary>
/// <remarks>
/// The rule ids and ceilings are the shipped ones where they matter: rule 4's ceilings run to 10,000
/// (25024 따뜻한 히라마 스튜 재료) so it has to stay a count on one instance, rule 6 authors
/// <c>max_stack</c> 1 on 9,527 of its 9,942 rows so it has to be one instance per caster, and rule 7 is
/// the instance-per-application rule (28644 동상, 10). The buff ids here are fakes; the shapes are live.
/// </remarks>
[NotInParallel]
public class BuffStackAddBuffTests
{
    private const uint IndependentBuffId = 92001;
    private const uint MultipleBuffId = 92002;
    private const uint MultipleDecreaseOneBuffId = 92003;
    private const uint MultipleDecreaseOneCeiling2BuffId = 92010;
    private const uint ExtendBuffId = 92004;
    private const uint ChargeExtendBuffId = 92005;
    private const uint ChargeExtendUncappedBuffId = 92006;
    private const uint RefreshBuffId = 92007;
    private const uint UnknownRuleBuffId = 92008;
    private const uint PermanentExtendBuffId = 92009;
    // 22102/22200 노 젓기 at 12, 24999 향연수호전 마력 주입 at 200: Independent rows whose ceiling is
    // above 1, i.e. the 415 rows that do accumulate within one caster.
    private const uint IndependentCeilingBuffId = 92011;
    private const uint IndependentCeilingTransformBuffId = 92012;
    // 11145 앞 돛 접힘SB3: max_stack 10 at duration 0, where the refresh guard used to drop the repeat
    // application entirely.
    private const uint IndependentPermanentCeilingBuffId = 92013;
    // 1831 석화 독 at 5, 24621 카둠의 치명적인 독 at 20: a counted family with a transform, which is
    // where removing "the first instance with this buff id" took the wrong caster's copy.
    private const uint MultipleTransformBuffId = 92014;
    private const uint MultipleTransformTargetBuffId = 92015;
    // 28644 동상 -> 28645 동결: separate instances, so the transform is noticed at the next application
    // and every member of the arriving caster's family has to go.
    private const uint Rule7TransformBuffId = 92016;
    private const uint Rule7TransformTargetBuffId = 92017;

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
            // 6286 체력 회복 / 29209 투코의 치유 shape: one live instance per caster, max_stack 1.
            [IndependentBuffId] = new BuffTemplate
            {
                Id = IndependentBuffId, Duration = 5000, StackRule = BuffStackRule.Independent,
                MaxStack = 1, Kind = BuffKind.Good
            },
            // 20860 해풍 응용 / 25024 따뜻한 히라마 스튜 재료 shape: one counted instance per caster.
            [MultipleBuffId] = new BuffTemplate
            {
                Id = MultipleBuffId, Duration = 60000, StackRule = BuffStackRule.Multiple,
                MaxStack = 5, Kind = BuffKind.Good
            },
            // 28644 동상 shape: separate instances, capped at 10.
            [MultipleDecreaseOneBuffId] = new BuffTemplate
            {
                Id = MultipleDecreaseOneBuffId, Duration = 30000,
                StackRule = BuffStackRule.MultipleDecreaseOne, MaxStack = 10, Kind = BuffKind.Bad
            },
            // 24879 표적 shape: the same rule with a two-instance ceiling.
            [MultipleDecreaseOneCeiling2BuffId] = new BuffTemplate
            {
                Id = MultipleDecreaseOneCeiling2BuffId, Duration = 30000,
                StackRule = BuffStackRule.MultipleDecreaseOne, MaxStack = 2, Kind = BuffKind.Bad
            },
            // 4841 연료 주입 shape: the application adds its duration instead of replacing the instance.
            [ExtendBuffId] = new BuffTemplate
            {
                Id = ExtendBuffId, Duration = 5000, StackRule = BuffStackRule.Extend,
                MaxStack = 1, Kind = BuffKind.Good
            },
            [PermanentExtendBuffId] = new BuffTemplate
            {
                Id = PermanentExtendBuffId, Duration = 0, StackRule = BuffStackRule.Extend,
                MaxStack = 1, Kind = BuffKind.Good
            },
            // 864 근성 / 22574 보호막 shape: charge sums, held at max_charge.
            [ChargeExtendBuffId] = new BuffTemplate
            {
                Id = ChargeExtendBuffId, Duration = 60000, StackRule = BuffStackRule.ChargeExtend,
                MaxStack = 1, MaxCharge = 5, InitMinCharge = 3, InitMaxCharge = 3, Kind = BuffKind.Good
            },
            // 35 차원의 틈 shape: max_charge 0, so there is no ceiling to hold the sum at.
            [ChargeExtendUncappedBuffId] = new BuffTemplate
            {
                Id = ChargeExtendUncappedBuffId, Duration = 60000,
                StackRule = BuffStackRule.ChargeExtend, MaxStack = 1, MaxCharge = 0,
                InitMinCharge = 4, InitMaxCharge = 4, Kind = BuffKind.Good
            },
            // 4053 낚시 shape: refresh replaces, it never adds.
            [RefreshBuffId] = new BuffTemplate
            {
                Id = RefreshBuffId, Duration = 5000, StackRule = BuffStackRule.Refresh,
                MaxStack = 1, Kind = BuffKind.Good
            },
            // An id the content table does not name: the branch that existed before these rules were
            // split out, and the one a future rule id lands in.
            [UnknownRuleBuffId] = new BuffTemplate
            {
                Id = UnknownRuleBuffId, Duration = 60000, StackRule = (BuffStackRule)99,
                MaxStack = 4, Kind = BuffKind.Good
            },
            // 22102/22200 노 젓기 shape: Independent, per caster, but the caster's own repeats count up to
            // the ceiling rather than refreshing a single application.
            [IndependentCeilingBuffId] = new BuffTemplate
            {
                Id = IndependentCeilingBuffId, Duration = 20000, StackRule = BuffStackRule.Independent,
                MaxStack = 3, Kind = BuffKind.Good
            },
            [IndependentCeilingTransformBuffId] = new BuffTemplate
            {
                Id = IndependentCeilingTransformBuffId, Duration = 20000,
                StackRule = BuffStackRule.Independent, MaxStack = 2, Kind = BuffKind.Good,
                TransformBuffId = IndependentCeilingBuffId
            },
            // 11145 앞 돛 접힘SB3 shape: Independent, ceiling above 1, duration 0.
            [IndependentPermanentCeilingBuffId] = new BuffTemplate
            {
                Id = IndependentPermanentCeilingBuffId, Duration = 0, StackRule = BuffStackRule.Independent,
                MaxStack = 10, Kind = BuffKind.Good
            },
            // 1831 석화 독 shape: a counted family per caster whose ceiling names a transform.
            [MultipleTransformBuffId] = new BuffTemplate
            {
                Id = MultipleTransformBuffId, Duration = 30000, StackRule = BuffStackRule.Multiple,
                MaxStack = 2, Kind = BuffKind.Bad, TransformBuffId = MultipleTransformTargetBuffId
            },
            [MultipleTransformTargetBuffId] = new BuffTemplate
            {
                Id = MultipleTransformTargetBuffId, Duration = 30000, StackRule = BuffStackRule.Refresh,
                MaxStack = 1, Kind = BuffKind.Bad
            },
            // 28644 동상 -> 28645 동결 shape: separate instances, so the ceiling is noticed on the
            // application that cannot add another member.
            [Rule7TransformBuffId] = new BuffTemplate
            {
                Id = Rule7TransformBuffId, Duration = 30000,
                StackRule = BuffStackRule.MultipleDecreaseOne, MaxStack = 2, Kind = BuffKind.Bad,
                TransformBuffId = Rule7TransformTargetBuffId
            },
            [Rule7TransformTargetBuffId] = new BuffTemplate
            {
                Id = Rule7TransformTargetBuffId, Duration = 30000, StackRule = BuffStackRule.Refresh,
                MaxStack = 1, Kind = BuffKind.Bad
            }
        });
        _skillManagerField.SetValue(null, skillManager);

        var buffGameData = new BuffGameData();
        SetField(buffGameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(buffGameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(buffGameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        _buffGameDataField.SetValue(null, buffGameData);

        // Timed buffs schedule their own expiry and a refresh clears the pending one; neither may reach
        // a real scheduler from a test.
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
    public async Task AddBuff_IndependentFamily_TwoCastersKeepTheirOwnInstance()
    {
        // The regression this rule exists for: the second caster used to find the first caster's live
        // instance, fail to grow it (max_stack 1) and overwrite it, so one DoT survived a raid.
        var (owner, casterA, casterB) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, casterA, IndependentBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, casterB, IndependentBuffId));

        var instances = InstancesOf(owner, IndependentBuffId);
        await Assert.That(instances.Count).IsEqualTo(2);
        await Assert.That(owner.Buffs.GetBuffCountById(IndependentBuffId)).IsEqualTo(2);
        // The skill caster is what identifies the caster on the wire, and Caster is null for a source
        // that is not a Unit (a doodad, an item), which is why the rule reads both.
        await Assert.That(instances.Select(i => i.SkillCaster.ObjId).OrderBy(id => id))
            .IsEquivalentTo(new uint[] { casterA.ObjId, casterB.ObjId });
        await Assert.That(instances[0].Index).IsNotEqualTo(instances[1].Index);

        // Neither icon may claim the other caster's application.
        await Assert.That(instances[0].StackCount).IsEqualTo(1u);
        await Assert.That(instances[1].StackCount).IsEqualTo(1u);
    }

    [Test]
    public async Task AddBuff_IndependentFamily_SameCasterTwiceRefreshesThatInstance()
    {
        // Independent does not count: 9,527 of its 9,942 rows author max_stack 1, so a caster's repeat
        // application refreshes its own instance rather than growing a stack on it.
        var (owner, caster, _) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, caster, IndependentBuffId), forcedDuration: 5000);
        var first = InstancesOf(owner, IndependentBuffId)[0];
        first.StartTime = DateTime.UtcNow.AddMilliseconds(-4000);

        owner.Buffs.AddBuff(CreateBuff(owner, caster, IndependentBuffId), forcedDuration: 5000);

        var instances = InstancesOf(owner, IndependentBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Index).IsEqualTo(first.Index);
        await Assert.That(instances[0].Stack).IsEqualTo(1);
        await Assert.That(instances[0].Duration).IsEqualTo(5000);
    }

    [Test]
    public async Task AddBuff_MultipleFamily_TwoCastersKeepTheirOwnCountedInstance()
    {
        // Rule 4 is the counting rule, keyed on the caster: one caster's applications accumulate on its
        // own instance (the sail-wind family reaches 60 that way), and a second caster holds its own
        // rather than adding to the first — so an expiry takes one instance, not the whole family.
        var (owner, casterA, casterB) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, casterA, MultipleBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, casterA, MultipleBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, casterB, MultipleBuffId));

        var instances = InstancesOf(owner, MultipleBuffId);
        await Assert.That(instances.Count).IsEqualTo(2);
        await Assert.That(owner.Buffs.GetBuffCountById(MultipleBuffId)).IsEqualTo(3);

        var fromA = instances.Single(i => i.SkillCaster.ObjId == casterA.ObjId);
        var fromB = instances.Single(i => i.SkillCaster.ObjId == casterB.ObjId);
        await Assert.That(fromA.Stack).IsEqualTo(2);
        await Assert.That(fromB.Stack).IsEqualTo(1);
        await Assert.That(fromA.StackCount).IsEqualTo(2u);
        await Assert.That(fromB.StackCount).IsEqualTo(1u);
    }

    [Test]
    public async Task AddBuff_MultipleFamily_CountsUpToItsCeilingAndStops()
    {
        // Five applications of a max_stack 5 family stay one instance carrying five; the sixth is
        // absorbed by the refresh (a timed family) instead of creeping past the ceiling.
        var (owner, caster, _) = CreateUnits();

        for (var i = 0; i < 6; i++)
            owner.Buffs.AddBuff(CreateBuff(owner, caster, MultipleBuffId));

        var instances = InstancesOf(owner, MultipleBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Stack).IsEqualTo(5);
        await Assert.That(instances[0].StackCount).IsEqualTo(5u);
    }

    [Test]
    public async Task AddBuff_MultipleDecreaseOneFamily_KeepsOneInstancePerApplication()
    {
        // Rule 7's applications do not collapse: two casts are two instances with their own timers.
        var (owner, caster, _) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, caster, MultipleDecreaseOneBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, caster, MultipleDecreaseOneBuffId));

        var instances = InstancesOf(owner, MultipleDecreaseOneBuffId);
        await Assert.That(instances.Count).IsEqualTo(2);
        await Assert.That(instances[0].Index).IsNotEqualTo(instances[1].Index);
        await Assert.That(instances[0].Stack).IsEqualTo(1);
    }

    [Test]
    public async Task AddBuff_MultipleDecreaseOneFamily_LosesExactlyOneInstanceWhenOneExpires()
    {
        // The point of the rule: one expiry takes one stack off, not the family.
        var (owner, caster, _) = CreateUnits();
        owner.Buffs.AddBuff(CreateBuff(owner, caster, MultipleDecreaseOneBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, caster, MultipleDecreaseOneBuffId));
        var before = InstancesOf(owner, MultipleDecreaseOneBuffId);
        await Assert.That(before.Count).IsEqualTo(2);

        before[0].Exit();

        var after = InstancesOf(owner, MultipleDecreaseOneBuffId);
        await Assert.That(after.Count).IsEqualTo(1);
        await Assert.That(after[0].Index).IsEqualTo(before[1].Index);
        await Assert.That(owner.Buffs.GetBuffCountById(MultipleDecreaseOneBuffId)).IsEqualTo(1);
    }

    [Test]
    public async Task AddBuff_MultipleDecreaseOneFamily_AtTheCeilingRefreshesItsOldestInstance()
    {
        // max_stack 2 here: the third application has no room for a third instance, so it goes to the
        // one nearest to expiring rather than being dropped or doubling the family.
        var (owner, caster, _) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, caster, MultipleDecreaseOneCeiling2BuffId), forcedDuration: 30000);
        owner.Buffs.AddBuff(CreateBuff(owner, caster, MultipleDecreaseOneCeiling2BuffId), forcedDuration: 30000);
        var oldest = InstancesOf(owner, MultipleDecreaseOneCeiling2BuffId)[0];
        oldest.StartTime = DateTime.UtcNow.AddMilliseconds(-25000);

        owner.Buffs.AddBuff(CreateBuff(owner, caster, MultipleDecreaseOneCeiling2BuffId), forcedDuration: 30000);

        var instances = InstancesOf(owner, MultipleDecreaseOneCeiling2BuffId);
        await Assert.That(instances.Count).IsEqualTo(2);
        await Assert.That(instances.Single(i => i.Index == oldest.Index).Duration).IsEqualTo(30000);
    }

    [Test]
    public async Task AddBuff_ExtendFamily_AddsTheIncomingDurationToWhatIsLeft()
    {
        // 2 s left of the live instance plus an incoming 5 s application is 7 s of buff, where refresh
        // would leave 5 s.
        var (owner, caster, _) = CreateUnits();
        owner.Buffs.AddBuff(CreateBuff(owner, caster, ExtendBuffId), forcedDuration: 5000);
        var live = InstancesOf(owner, ExtendBuffId)[0];
        live.StartTime = DateTime.UtcNow.AddMilliseconds(-2000);
        var index = live.Index;

        owner.Buffs.AddBuff(CreateBuff(owner, caster, ExtendBuffId), forcedDuration: 5000);

        var instances = InstancesOf(owner, ExtendBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Index).IsEqualTo(index);
        await Assert.That(instances[0].Duration).IsGreaterThanOrEqualTo(7900)
            .And.IsLessThanOrEqualTo(8100);
    }

    [Test]
    public async Task AddBuff_RefreshFamily_ReplacesTheDurationInsteadOfAdding()
    {
        // The contrast with Extend, and the behaviour 19,716 buffs depend on: same setup, same elapsed
        // time, and the instance comes out at the incoming 5 s rather than 7 s.
        var (owner, caster, _) = CreateUnits();
        owner.Buffs.AddBuff(CreateBuff(owner, caster, RefreshBuffId), forcedDuration: 5000);
        var live = InstancesOf(owner, RefreshBuffId)[0];
        live.StartTime = DateTime.UtcNow.AddMilliseconds(-2000);

        owner.Buffs.AddBuff(CreateBuff(owner, caster, RefreshBuffId), forcedDuration: 5000);

        var instances = InstancesOf(owner, RefreshBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Duration).IsEqualTo(5000);
    }

    [Test]
    public async Task AddBuff_ExtendFamilyOfPermanentInstances_LeavesTheLiveInstanceAlone()
    {
        // Both sides are duration 0, so there is nothing to lengthen and GetTimeLeft() is the -1
        // sentinel. Overwriting would schedule a dispel in the past and drop the buff.
        var (owner, caster, _) = CreateUnits();
        owner.Buffs.AddBuff(CreateBuff(owner, caster, PermanentExtendBuffId));
        var live = InstancesOf(owner, PermanentExtendBuffId)[0];
        var startedAt = live.StartTime;

        owner.Buffs.AddBuff(CreateBuff(owner, caster, PermanentExtendBuffId));

        var instances = InstancesOf(owner, PermanentExtendBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Index).IsEqualTo(live.Index);
        await Assert.That(instances[0].Duration).IsEqualTo(0);
        await Assert.That(instances[0].StartTime).IsEqualTo(startedAt);
    }

    [Test]
    public async Task AddBuff_ChargeExtendFamily_SumsChargesAndHoldsThemAtTheCeiling()
    {
        // 864 근성 sums its charge into one instance; the ceiling is max_charge, so 3 + 3 lands on 5
        // and stays there.
        var (owner, caster, _) = CreateUnits();
        owner.Buffs.AddBuff(CreateBuff(owner, caster, ChargeExtendBuffId));
        var live = InstancesOf(owner, ChargeExtendBuffId)[0];
        await Assert.That(live.Charge).IsEqualTo(3);

        owner.Buffs.AddBuff(CreateBuff(owner, caster, ChargeExtendBuffId));

        var instances = InstancesOf(owner, ChargeExtendBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Charge).IsEqualTo(5);

        owner.Buffs.AddBuff(CreateBuff(owner, caster, ChargeExtendBuffId));
        await Assert.That(InstancesOf(owner, ChargeExtendBuffId)[0].Charge).IsEqualTo(5);
    }

    [Test]
    public async Task AddBuff_ChargeExtendFamilyWithoutACeiling_KeepsSumming()
    {
        // max_charge 0 on four of the 39 rows (35 차원의 틈 among them) means no ceiling was authored.
        var (owner, caster, _) = CreateUnits();
        owner.Buffs.AddBuff(CreateBuff(owner, caster, ChargeExtendUncappedBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, caster, ChargeExtendUncappedBuffId));

        var instances = InstancesOf(owner, ChargeExtendUncappedBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Charge).IsEqualTo(8);
    }

    [Test]
    public async Task AddBuff_UnknownRuleId_KeepsTheOneInstanceBehaviour()
    {
        // A stack_rule_id this build does not know still gets the pre-split path: one instance for the
        // family carrying the count, whatever the caster. Nothing new can explode into instances.
        var (owner, casterA, casterB) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, casterA, UnknownRuleBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, casterB, UnknownRuleBuffId));

        var instances = InstancesOf(owner, UnknownRuleBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Stack).IsEqualTo(2);
        await Assert.That(instances[0].StackCount).IsEqualTo(2u);
    }

    /// <summary>
    /// An Independent family whose ceiling is above 1 has to accumulate for its own caster. The first
    /// version of this rule refreshed the caster's instance unconditionally, which pinned 22102/22200
    /// 노 젓기 (12), 24999 향연수호전 마력 주입 (200) and 14841 (10) at one application.
    /// </summary>
    [Test]
    public async Task AddBuff_IndependentFamilyWithACeiling_CountsUpToItForItsOwnCaster()
    {
        var (owner, casterA, casterB) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, casterA, IndependentCeilingBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, casterA, IndependentCeilingBuffId));

        var mine = InstancesOf(owner, IndependentCeilingBuffId);
        await Assert.That(mine.Count).IsEqualTo(1);
        await Assert.That(mine[0].Stack).IsEqualTo(2);

        // A second caster still gets its own instance rather than adding to this count.
        owner.Buffs.AddBuff(CreateBuff(owner, casterB, IndependentCeilingBuffId));
        await Assert.That(InstancesOf(owner, IndependentCeilingBuffId).Count).IsEqualTo(2);

        // And the ceiling holds: the fourth application for caster A leaves the count at MaxStack.
        owner.Buffs.AddBuff(CreateBuff(owner, casterA, IndependentCeilingBuffId));
        var first = InstancesOf(owner, IndependentCeilingBuffId)
            .Single(i => i.SkillCaster.ObjId == casterA.ObjId);
        await Assert.That(first.Stack).IsEqualTo(3);
    }

    /// <summary>
    /// 11145 앞 돛 접힘SB3 is Independent, ceiling 10, duration 0. The refresh guard
    /// (<c>ShouldOverwriteOnRefresh</c>) refuses a permanent instance, so the repeat application used to
    /// be dropped entirely instead of counting.
    /// </summary>
    [Test]
    public async Task AddBuff_IndependentPermanentFamilyWithACeiling_StillCounts()
    {
        var (owner, caster, _) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, caster, IndependentPermanentCeilingBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, caster, IndependentPermanentCeilingBuffId));

        var instances = InstancesOf(owner, IndependentPermanentCeilingBuffId);
        await Assert.That(instances.Count).IsEqualTo(1);
        await Assert.That(instances[0].Stack).IsEqualTo(2);
    }

    /// <summary>
    /// A counted family that transforms must consume the instance that reached the ceiling. RemoveBuff
    /// takes the first instance with that buff id, which with two casters stacking the same debuff
    /// dispelled the other caster's copy while the one that topped out stayed live under the transform
    /// (1831 석화 독 at 5, 24621 카둠의 치명적인 독 at 20, 5193 허점 at 5).
    /// </summary>
    [Test]
    public async Task AddBuff_CountedFamilyThatTransforms_ConsumesTheInstanceAtTheCeiling()
    {
        var (owner, casterA, casterB) = CreateUnits();

        // Caster A opens the family and caster B joins with its own instance, so A's is first on the list.
        owner.Buffs.AddBuff(CreateBuff(owner, casterA, MultipleTransformBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, casterB, MultipleTransformBuffId));

        var family = InstancesOf(owner, MultipleTransformBuffId);
        await Assert.That(family.Count).IsEqualTo(2);
        await Assert.That(family[0].SkillCaster.ObjId).IsEqualTo(casterA.ObjId);

        // A applies once more: that reaches A's own ceiling of 2, so A's instance transforms and B's —
        // the one a first-match removal would have taken later in the list — stays live.
        owner.Buffs.AddBuff(CreateBuff(owner, casterA, MultipleTransformBuffId));

        var left = InstancesOf(owner, MultipleTransformBuffId);
        await Assert.That(left.Count).IsEqualTo(1);
        await Assert.That(left[0].SkillCaster.ObjId).IsEqualTo(casterB.ObjId);
        await Assert.That(InstancesOf(owner, MultipleTransformTargetBuffId).Count).IsEqualTo(1);
    }

    /// <summary>
    /// Rule 7 keeps one instance per application, so the transform is noticed on the application that
    /// cannot add another member, and every member of the arriving caster's family goes — each one
    /// ended once. RemoveEffect on a live instance re-entered through StopEffectTask and stripped a
    /// modifier-cache entry belonging to another live instance of the same family.
    /// </summary>
    [Test]
    public async Task AddBuff_Rule7FamilyThatTransforms_EndsItsMembersWithoutTakingAnotherCasters()
    {
        var (owner, casterA, casterB) = CreateUnits();

        owner.Buffs.AddBuff(CreateBuff(owner, casterA, Rule7TransformBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, casterA, Rule7TransformBuffId));
        owner.Buffs.AddBuff(CreateBuff(owner, casterB, Rule7TransformBuffId));

        var before = InstancesOf(owner, Rule7TransformBuffId);
        await Assert.That(before.Count).IsEqualTo(3);

        // A's third application fills A's own ceiling of 2, so A's two instances transform.
        owner.Buffs.AddBuff(CreateBuff(owner, casterA, Rule7TransformBuffId));

        var left = InstancesOf(owner, Rule7TransformBuffId);
        await Assert.That(left.Count).IsEqualTo(1);
        await Assert.That(left[0].SkillCaster.ObjId).IsEqualTo(casterB.ObjId);
        await Assert.That(InstancesOf(owner, Rule7TransformTargetBuffId).Count).IsEqualTo(1);
    }

    private static (BaseUnit Owner, BaseUnit CasterA, BaseUnit CasterB) CreateUnits() =>
        (new BaseUnit { ObjId = 1 }, new BaseUnit { ObjId = 2 }, new BaseUnit { ObjId = 3 });

    private static Buff CreateBuff(BaseUnit owner, BaseUnit caster, uint buffId) =>
        new(owner, caster, new SkillCasterUnit(caster.ObjId),
            SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow)
        {
            // Keeps SCBuffCreated/SCBuffUpdated/SCBuffRemoved and the zone relay out of a test with no
            // connection.
            Passive = true,
            AbLevel = 1
        };

    private static List<Buff> InstancesOf(BaseUnit owner, uint buffId) =>
        owner.Buffs.GetEffectsByType(typeof(BuffTemplate))
            .Where(buff => buff.Template.BuffId == buffId)
            .ToList();

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
