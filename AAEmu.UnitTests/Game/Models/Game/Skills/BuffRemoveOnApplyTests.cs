using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The <c>remove_on_*</c> flags and <c>buff_breakers</c> at the level they act: a real
/// <see cref="Buffs"/> on a real unit, driven through <see cref="Buffs.AddBuff"/> and
/// <see cref="Buffs.TriggerRemoveOn"/>. The pure decisions are in
/// <see cref="BuffRemoveOnRulesTests"/>; what is asserted here is that the live path raises them.
/// </summary>
/// <remarks>
/// Buff ids and tags are fakes; the shapes and the row counts behind them are the live ones — the
/// umbrella-plus-narrow flag pairs (31/31 attack spell-dot carriers also carry attack etc), the
/// 719-of-771 buffs naming attach point 1 Driver, the four <c>remove_on_change_equipments</c> masks, and
/// the <c>buff_breakers</c> direction (tag 4526 망명자 지우는 버프 erases the 망명자 buffs).
/// </remarks>
[NotInParallel]
public class BuffRemoveOnApplyTests
{
    private const uint VictimBuffId = 93001;
    private const uint BreakerBuffId = 93002;
    private const uint UnrelatedBuffId = 93003;
    private const uint ReGrantBuffId = 93004;
    private const uint ImmunityGrantBuffId = 93005;
    private const uint IndependentVictimBuffId = 93006;
    private const uint SourceDeadFamilyBuffId = 93007;
    private const uint UnmountPointBuffId = 93008;
    private const uint UnmountAnySeatBuffId = 93009;
    private const uint SummonedBuffId = 93010;
    private const uint RangedMaskBuffId = 93011;
    private const uint WideMaskBuffId = 93012;
    private const uint ExistingFlagBuffId = 93013;
    private const uint ExemptBuffId = 93014;
    private const uint PerCasterBreakerBuffId = 93015;
    private const uint FlagBuffBaseId = 93100;
    private const int DurationMs = 600000;

    private const uint VictimTagId = 7001;
    private const uint BreakerTagId = 7002;
    private const uint UnrelatedTagId = 7003;
    private const uint ReGrantTagId = 7004;
    private const uint IndependentVictimTagId = 7005;
    private const uint PerCasterBreakerTagId = 7006;

    private const uint OwnerObjId = 10u;
    private const uint CasterObjId = 20u;
    private const uint OtherCasterObjId = 21u;

    /// <summary>
    /// Every flag that had no raiser before this change, with the <c>TriggerRemoveOn</c> value it needs.
    /// <c>remove_on_exempt</c> is deliberately absent — see
    /// <see cref="Exempt_FlagIsReachable_ButNothingInThisBuildRaisesIt"/>.
    /// </summary>
    private static readonly (BuffRemoveOn Flag, uint Value)[] NewFlagCases =
    [
        (BuffRemoveOn.SourceDead, CasterObjId),
        (BuffRemoveOn.AutoAttack, 0),
        (BuffRemoveOn.AttackBuffTrigger, 0),
        (BuffRemoveOn.AttackedBuffTrigger, 0),
        (BuffRemoveOn.DamageBuffTrigger, 0),
        (BuffRemoveOn.DamagedBuffTrigger, 0),
        (BuffRemoveOn.AttackSpellDot, 0),
        (BuffRemoveOn.AttackEtcDot, 0),
        (BuffRemoveOn.AttackedSpellDot, 0),
        (BuffRemoveOn.AttackedEtcDot, 0),
        (BuffRemoveOn.DamageSpellDot, 0),
        (BuffRemoveOn.DamageEtcDot, 0),
        (BuffRemoveOn.DamagedSpellDot, 0),
        (BuffRemoveOn.DamagedEtcDot, 0)
    ];

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

        var buffs = new Dictionary<uint, BuffTemplate>
        {
            [VictimBuffId] = Good(VictimBuffId),
            [BreakerBuffId] = Good(BreakerBuffId),
            [UnrelatedBuffId] = Good(UnrelatedBuffId),
            [ReGrantBuffId] = Good(ReGrantBuffId),
            [ImmunityGrantBuffId] = Good(ImmunityGrantBuffId),
            // 6286 체력 회복 / 24032 동상 shape: rule 6 keeps one instance per caster.
            [IndependentVictimBuffId] = Good(IndependentVictimBuffId, BuffStackRule.Independent),
            [SourceDeadFamilyBuffId] = Good(SourceDeadFamilyBuffId, BuffStackRule.Independent,
                removeOnSourceDead: true),
            // 719 of the 771 buffs naming a seat name 1 Driver.
            [UnmountPointBuffId] = Good(UnmountPointBuffId, removeOnUnmount: true, unmountAttachPointId: 1),
            // The other 130 remove_on_unmount carriers name no seat.
            [UnmountAnySeatBuffId] = Good(UnmountAnySeatBuffId, removeOnUnmount: true),
            [SummonedBuffId] = Good(SummonedBuffId, removeBySummoned: true),
            // 15733/29615 감정 표현_궁수: bit 17 Ranged.
            [RangedMaskBuffId] = Good(RangedMaskBuffId, changeEquipmentsMask: 131072),
            // 29238 잠재능력 발현: every armour, weapon and cosplay slot.
            [WideMaskBuffId] = Good(WideMaskBuffId, changeEquipmentsMask: 134733823),
            [ExistingFlagBuffId] = Good(ExistingFlagBuffId, removeOnMove: true),
            [ExemptBuffId] = Good(ExemptBuffId, removeOnExempt: true),
            [PerCasterBreakerBuffId] = Good(PerCasterBreakerBuffId)
        };
        foreach (var (flag, _) in NewFlagCases)
            buffs[FlagBuffId(flag)] = FlagTemplate(flag);

        SetField(skillManager, "_buffs", buffs);
        // tagged_buffs: the tags each buff carries.
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>
        {
            [VictimBuffId] = [VictimTagId],
            [BreakerBuffId] = [BreakerTagId],
            [UnrelatedBuffId] = [UnrelatedTagId],
            [ReGrantBuffId] = [ReGrantTagId],
            [ImmunityGrantBuffId] = [UnrelatedTagId],
            [IndependentVictimBuffId] = [IndependentVictimTagId],
            [PerCasterBreakerBuffId] = [PerCasterBreakerTagId]
        });
        SetField(skillManager, "_taggedBuffs", new Dictionary<uint, List<uint>>
        {
            [VictimTagId] = [VictimBuffId],
            [BreakerTagId] = [BreakerBuffId],
            [UnrelatedTagId] = [UnrelatedBuffId, ImmunityGrantBuffId],
            [ReGrantTagId] = [ReGrantBuffId],
            [IndependentVictimTagId] = [IndependentVictimBuffId]
        });
        // buff_breakers read by the arriving buff's tag. ReGrantTagId also names its own carrier, which is
        // the shape of the 29 self-referential rows.
        SetField(skillManager, "_buffBreakers", new Dictionary<uint, List<uint>>
        {
            [BreakerTagId] = [VictimBuffId],
            [ReGrantTagId] = [ReGrantBuffId],
            [PerCasterBreakerTagId] = [IndependentVictimBuffId]
        });
        // tagged_immune_buffs: while the grant is up, a candidate carrying the tag is refused.
        SetField(skillManager, "_buffImmunityTags", new Dictionary<uint, List<uint>>
        {
            [ImmunityGrantBuffId] = [BreakerTagId]
        });
        SetField(skillManager, "_requiredBuffTags", new Dictionary<uint, List<uint>>());
        SetField(skillManager, "_buffTriggers", new Dictionary<uint, List<BuffTriggerTemplate>>());
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

    // ---------------------------------------------------------------- buff_breakers

    [Test]
    public async Task AddBuff_BuffCarryingABreakerTag_LandsAndRemovesTheBuffItBreaks()
    {
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, VictimBuffId);

        ApplyBuff(owner, caster, BreakerBuffId);

        await Assert.That(owner.Buffs.CheckBuff(VictimBuffId)).IsFalse();
        await Assert.That(owner.Buffs.CheckBuff(BreakerBuffId)).IsTrue();
    }

    [Test]
    public async Task AddBuff_BuffCarryingAnUnrelatedTag_LeavesTheBuffAlone()
    {
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, VictimBuffId);

        ApplyBuff(owner, caster, UnrelatedBuffId);

        await Assert.That(owner.Buffs.CheckBuff(VictimBuffId)).IsTrue();
        await Assert.That(owner.Buffs.CheckBuff(UnrelatedBuffId)).IsTrue();
    }

    [Test]
    public async Task AddBuff_BreakerOnAnotherCastersInstance_RemovesItToo()
    {
        // The row names a buff, not one caster's copy of it, so a victim another caster applied goes as
        // well: the arriving buff is normally somebody else's (a stun landing on the unit performing).
        var (owner, caster) = CreateUnits();
        var otherCaster = new Unit { ObjId = OtherCasterObjId };
        ApplyBuff(owner, otherCaster, VictimBuffId);

        ApplyBuff(owner, caster, BreakerBuffId);

        await Assert.That(owner.Buffs.CheckBuff(VictimBuffId)).IsFalse();
    }

    [Test]
    public async Task AddBuff_BreakerRowNamingItsOwnBuffId_RemovesThePreviousInstanceAndKeepsTheArrival()
    {
        // 29 buff_breakers rows name the arriving buff's own id — the glove techniques 30034-30192, the
        // 아리아의 춤동작 steps 16395-16399, 26345 석상 체크 완료 해제. They clear the previous member of a
        // re-grant family, so the arrival has to survive them.
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, ReGrantBuffId);
        await Assert.That(owner.Buffs.GetBuffCountById(ReGrantBuffId)).IsEqualTo(1);

        ApplyBuff(owner, caster, ReGrantBuffId);

        await Assert.That(owner.Buffs.CheckBuff(ReGrantBuffId)).IsTrue();
        await Assert.That(owner.Buffs.GetBuffCountById(ReGrantBuffId)).IsEqualTo(1);
    }

    [Test]
    public async Task AddBuff_BreakerOnAPerCasterFamily_TakesEveryLiveInstance()
    {
        // Rule 6 keeps one instance per caster, so the victim family is live twice here; the arriving
        // breaker's tag lists the family, and both instances go while the arrival stays.
        var (owner, caster) = CreateUnits();
        var otherCaster = new Unit { ObjId = OtherCasterObjId };
        ApplyBuff(owner, caster, IndependentVictimBuffId);
        ApplyBuff(owner, otherCaster, IndependentVictimBuffId);
        await Assert.That(owner.Buffs.GetBuffCountById(IndependentVictimBuffId)).IsEqualTo(2);

        ApplyBuff(owner, caster, PerCasterBreakerBuffId);

        await Assert.That(owner.Buffs.CheckBuff(IndependentVictimBuffId)).IsFalse();
        await Assert.That(owner.Buffs.CheckBuff(PerCasterBreakerBuffId)).IsTrue();
    }

    [Test]
    public async Task AddBuff_BreakerRefusedByTagImmunity_BreaksNothing()
    {
        // The order is immunity refusal -> require-tag refusal -> breaker removal -> stack rule, so a
        // refused buff never lands and never breaks. This drives the real refusing path,
        // BuffTemplate.Apply, which is where the checks live for a trigger or item applied buff.
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, VictimBuffId);
        ApplyBuff(owner, caster, ImmunityGrantBuffId);

        var breaker = SkillManager.Instance.GetBuffTemplate(BreakerBuffId);
        breaker.Apply(caster, new SkillCasterUnit(caster.ObjId), owner, new SkillCastUnitTarget(owner.ObjId),
            new CastSkill(0, 1), new EffectSource(), new SkillObject(), DateTime.UtcNow);

        await Assert.That(owner.Buffs.CheckBuff(BreakerBuffId)).IsFalse();
        await Assert.That(owner.Buffs.CheckBuff(VictimBuffId)).IsTrue();
    }

    // ---------------------------------------------------------------- the flags that were never raised

    [Test]
    public async Task TriggerRemoveOn_EachNewFlag_RemovesItsOwnBuffAndNoOtherEventDoes()
    {
        var mismatches = new List<string>();
        var (owner, caster) = CreateUnits();

        foreach (var (flag, value) in NewFlagCases)
        {
            var buffId = FlagBuffId(flag);

            ApplyBuff(owner, caster, buffId);
            if (!owner.Buffs.CheckBuff(buffId))
                mismatches.Add($"{flag}: the buff did not land");

            owner.Buffs.TriggerRemoveOn(flag, value);
            if (owner.Buffs.CheckBuff(buffId))
                mismatches.Add($"{flag}: did not drop on its own event");

            ApplyBuff(owner, caster, buffId);
            // Move is on none of the fixtures, so a buff carrying only the flag under test must survive it.
            owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Move);
            if (!owner.Buffs.CheckBuff(buffId))
                mismatches.Add($"{flag}: dropped on an unrelated event");

            owner.Buffs.RemoveBuff(buffId);
        }

        await Assert.That(mismatches).IsEmpty();
    }

    [Test]
    public async Task SourceDead_CasterDies_EndsTheBuffItAppliedToAnotherUnit()
    {
        // The buff lives on the target while the unit that dies is somewhere else, which is what the
        // subscription on the caster's OnDeath is for. Unit.DoDie raises exactly this event.
        var (owner, caster) = CreateUnits();
        var otherCaster = new Unit { ObjId = OtherCasterObjId };
        ApplyBuff(owner, caster, FlagBuffId(BuffRemoveOn.SourceDead));
        await Assert.That(owner.Buffs.CheckBuff(FlagBuffId(BuffRemoveOn.SourceDead))).IsTrue();

        caster.Events.OnDeath(caster, new OnDeathArgs { Victim = caster, Killer = otherCaster });

        await Assert.That(owner.Buffs.CheckBuff(FlagBuffId(BuffRemoveOn.SourceDead))).IsFalse();
    }

    [Test]
    public async Task SourceDead_TwoCastersHoldTheSameFamily_OnlyTheDeadCastersInstanceGoes()
    {
        // This is the one removal scoped to a single caster: two casters hold their own instance of a
        // rule 6 family, and one of them dying must not end the other's.
        var owner = new BaseUnit { ObjId = OwnerObjId };
        var casterA = new Unit { ObjId = CasterObjId };
        var casterB = new Unit { ObjId = OtherCasterObjId };
        ApplyBuff(owner, casterA, SourceDeadFamilyBuffId);
        ApplyBuff(owner, casterB, SourceDeadFamilyBuffId);
        await Assert.That(owner.Buffs.GetBuffCountById(SourceDeadFamilyBuffId)).IsEqualTo(2);

        casterA.Events.OnDeath(casterA, new OnDeathArgs { Victim = casterA, Killer = casterB });

        await Assert.That(owner.Buffs.GetBuffCountById(SourceDeadFamilyBuffId)).IsEqualTo(1);
        await Assert.That(owner.Buffs.CheckBuff(SourceDeadFamilyBuffId)).IsTrue();
    }

    [Test]
    public async Task SourceDead_DeathOfAnUnrelatedUnit_LeavesTheBuffUp()
    {
        var (owner, caster) = CreateUnits();
        var other = new Unit { ObjId = OtherCasterObjId };
        ApplyBuff(owner, caster, FlagBuffId(BuffRemoveOn.SourceDead));

        other.Events.OnDeath(other, new OnDeathArgs { Victim = other, Killer = caster });

        await Assert.That(owner.Buffs.CheckBuff(FlagBuffId(BuffRemoveOn.SourceDead))).IsTrue();
    }

    [Test]
    public async Task SourceDead_BuffAppliedToItself_DropsWithItsOwner()
    {
        var owner = new Unit { ObjId = OwnerObjId };
        ApplyBuff(owner, owner, FlagBuffId(BuffRemoveOn.SourceDead));

        owner.Events.OnDeath(owner, new OnDeathArgs { Victim = owner, Killer = owner });

        await Assert.That(owner.Buffs.CheckBuff(FlagBuffId(BuffRemoveOn.SourceDead))).IsFalse();
    }

    [Test]
    public async Task TriggerRemoveOn_AlreadyRaisedFlag_StillRemovesItsBuff()
    {
        // The refactor of the 28-branch chain must not have moved the flags that already worked.
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, ExistingFlagBuffId);

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.UseSkill);
        await Assert.That(owner.Buffs.CheckBuff(ExistingFlagBuffId)).IsTrue();

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Move);
        await Assert.That(owner.Buffs.CheckBuff(ExistingFlagBuffId)).IsFalse();
    }

    [Test]
    public async Task Exempt_FlagIsReachable_ButNothingInThisBuildRaisesIt()
    {
        // remove_on_exempt is 7 755 buffs — the largest of the flags that had no raiser — and the flag is
        // wired through the rule, so a raise site can be added the moment the state exists. It has none to
        // come from today: Character carries CrimePoint, CrimeRecord and JuryPoint and nothing sets or
        // clears an exempt state, and the only other Exempt in the tree is the unrelated buffs.exempt
        // column (141 rows) and B3's immune_except_* grant exception. This test pins both halves: the flag
        // removes the buff when the event is raised by hand, and no code path raises it.
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, ExemptBuffId);

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Exempt);

        await Assert.That(owner.Buffs.CheckBuff(ExemptBuffId)).IsFalse();
    }

    // ---------------------------------------------------------------- the three columns with no consumer

    [Test]
    public async Task UnmountAttachPoint_BuffNamingASeat_DropsOnThatSeatOnly()
    {
        // 719 of the 771 buffs with a point name 1 Driver; the rest name passengers and 80 Telescope.
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, UnmountPointBuffId);

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount, 2);
        await Assert.That(owner.Buffs.CheckBuff(UnmountPointBuffId)).IsTrue();

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount, 1);
        await Assert.That(owner.Buffs.CheckBuff(UnmountPointBuffId)).IsFalse();
    }

    [Test]
    public async Task UnmountAttachPoint_BuffNamingNoSeat_KeepsTheAnySeatBehaviour()
    {
        // 130 of the 780 remove_on_unmount carriers name no seat, and an unmount whose seat the server
        // could not resolve (0) is the pre-existing case as well.
        var (owner, caster) = CreateUnits();

        ApplyBuff(owner, caster, UnmountAnySeatBuffId);
        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount, 2);
        await Assert.That(owner.Buffs.CheckBuff(UnmountAnySeatBuffId)).IsFalse();

        ApplyBuff(owner, caster, UnmountPointBuffId);
        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount, 0);
        await Assert.That(owner.Buffs.CheckBuff(UnmountPointBuffId)).IsFalse();
    }

    [Test]
    public async Task Summoned_PoseDropsWhenTheOwnerSummons()
    {
        // 110 buffs: the 감정 표현_* poses, 자세 잡기, 예도, 맹세, 위엄, 열정의 춤. Raised where a mate or
        // a slave is summoned (CharacterMates.SpawnMount, SlaveManager.Create).
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, SummonedBuffId);

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Mount);
        await Assert.That(owner.Buffs.CheckBuff(SummonedBuffId)).IsTrue();

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Summoned);
        await Assert.That(owner.Buffs.CheckBuff(SummonedBuffId)).IsFalse();
    }

    [Test]
    public async Task ChangeEquipments_BuffMaskingTheRangedSlot_DropsOnThatSlotOnly()
    {
        // 감정 표현_궁수 15733/29615 = bit 17 Ranged.
        var (owner, caster) = CreateUnits();
        ApplyBuff(owner, caster, RangedMaskBuffId);

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.ChangeEquipments, 15);
        await Assert.That(owner.Buffs.CheckBuff(RangedMaskBuffId)).IsTrue();

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.ChangeEquipments, 17);
        await Assert.That(owner.Buffs.CheckBuff(RangedMaskBuffId)).IsFalse();
    }

    [Test]
    public async Task ChangeEquipments_WideMask_DropsOnAnySlotItCoversAndNotOnTheOthers()
    {
        // 29238 잠재능력 발현 = 134733823, every armour, weapon and cosplay slot: 15 Mainhand is in it,
        // 13 Undershirt is not.
        var (owner, caster) = CreateUnits();

        ApplyBuff(owner, caster, WideMaskBuffId);
        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.ChangeEquipments, 13);
        await Assert.That(owner.Buffs.CheckBuff(WideMaskBuffId)).IsTrue();

        owner.Buffs.TriggerRemoveOn(BuffRemoveOn.ChangeEquipments, 15);
        await Assert.That(owner.Buffs.CheckBuff(WideMaskBuffId)).IsFalse();
    }

    // ---------------------------------------------------------------- fixtures

    private static (BaseUnit Owner, Unit Caster) CreateUnits() =>
        (new BaseUnit { ObjId = OwnerObjId }, new Unit { ObjId = CasterObjId });

    /// <summary>The fixture id each flag's template is registered under.</summary>
    private static uint FlagBuffId(BuffRemoveOn flag) => FlagBuffBaseId + (uint)flag;

    private static BuffTemplate Good(uint id, BuffStackRule stackRule = BuffStackRule.Refresh,
        bool removeOnSourceDead = false, bool removeOnMove = false, bool removeOnUnmount = false,
        int unmountAttachPointId = 0, bool removeBySummoned = false, long changeEquipmentsMask = 0,
        bool removeOnExempt = false) =>
        new()
        {
            Id = id,
            Duration = DurationMs,
            Kind = BuffKind.Good,
            StackRule = stackRule,
            MaxStack = 1,
            RemoveOnSourceDead = removeOnSourceDead,
            RemoveOnMove = removeOnMove,
            RemoveOnUnmount = removeOnUnmount,
            RemoveOnUnmountAttachPointId = unmountAttachPointId,
            RemoveBySummoned = removeBySummoned,
            RemoveOnChangeEquipments = changeEquipmentsMask,
            RemoveOnExempt = removeOnExempt
        };

    /// <summary>One template per never-raised flag, carrying that flag and nothing else.</summary>
    private static BuffTemplate FlagTemplate(BuffRemoveOn flag)
    {
        var id = FlagBuffId(flag);
        return flag switch
        {
            BuffRemoveOn.SourceDead => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnSourceDead = true },
            BuffRemoveOn.AutoAttack => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnAutoAttack = true },
            BuffRemoveOn.AttackBuffTrigger => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnAttackBuffTrigger = true },
            BuffRemoveOn.AttackedBuffTrigger => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnAttackedBuffTrigger = true },
            BuffRemoveOn.DamageBuffTrigger => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnDamageBuffTrigger = true },
            BuffRemoveOn.DamagedBuffTrigger => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnDamagedBuffTrigger = true },
            BuffRemoveOn.AttackSpellDot => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnAttackSpellDot = true },
            BuffRemoveOn.AttackEtcDot => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnAttackEtcDot = true },
            BuffRemoveOn.AttackedSpellDot => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnAttackedSpellDot = true },
            BuffRemoveOn.AttackedEtcDot => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnAttackedEtcDot = true },
            BuffRemoveOn.DamageSpellDot => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnDamageSpellDot = true },
            BuffRemoveOn.DamageEtcDot => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnDamageEtcDot = true },
            BuffRemoveOn.DamagedSpellDot => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnDamagedSpellDot = true },
            BuffRemoveOn.DamagedEtcDot => new BuffTemplate
            { Id = id, Duration = DurationMs, Kind = BuffKind.Good, RemoveOnDamagedEtcDot = true },
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "no fixture template for that flag")
        };
    }

    /// <summary>Puts a buff on the owner the way the game does, with packets kept out of the test.</summary>
    private static void ApplyBuff(BaseUnit owner, Unit caster, uint buffId)
    {
        var template = SkillManager.Instance.GetBuffTemplate(buffId);
        owner.Buffs.AddBuff(new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), template, null,
            DateTime.UtcNow)
        {
            Passive = true,
            AbLevel = 1
        });
    }

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
