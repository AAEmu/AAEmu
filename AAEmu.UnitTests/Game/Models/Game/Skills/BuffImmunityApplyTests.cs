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
/// The real path: a real <see cref="Buffs"/> on a real unit, an immunity buff actually applied, and a
/// candidate buff driven through <see cref="BuffTemplate.Apply"/> and <see cref="BuffEffect.Apply"/>.
/// These are the two call sites that used to consult the no-op <c>CheckBuffImmune(uint)</c>.
/// </summary>
/// <remarks>
/// The lookups are the real <see cref="SkillManager"/> and <see cref="BuffGameData"/> singletons with
/// their tables installed directly, so the rules, the tag lookups and the loaders' key shape are all
/// exercised; only the content database rows are faked. Ids follow the live data where the shape is
/// taken from it (93 동결 refuses tag 919 차가운 발걸음, 4627 가벼운 발걸음 requires tag 831 무겁다).
/// </remarks>
[NotInParallel]
public class BuffImmunityApplyTests
{
    private const uint ImmuneBuffId = 93; // 동결, grants immunity to tag 919 차가운 발걸음
    private const uint FreezeTagId = 919; // 차가운 발걸음
    private const uint TaggedCandidateId = 91001;
    private const uint UntaggedCandidateId = 91002;
    private const uint RequiredTagCandidateId = 91003;
    private const uint RequiredTagId = 831; // 무겁다
    private const uint RequiredTagCarrierId = 91004;
    private const uint CreatorExceptionBuffId = 91010;
    private const uint CreatorExceptionCandidateId = 91011;
    private const uint OwnerObjId = 1u;
    private const uint CasterObjId = 2u;

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
            [ImmuneBuffId] = new() { Id = ImmuneBuffId, Duration = 0 },
            [CreatorExceptionBuffId] = new() { Id = CreatorExceptionBuffId, Duration = 0, ImmuneExceptCreator = true },
            [TaggedCandidateId] = new() { Id = TaggedCandidateId, Duration = 10000 },
            [UntaggedCandidateId] = new() { Id = UntaggedCandidateId, Duration = 10000 },
            [RequiredTagCandidateId] = new() { Id = RequiredTagCandidateId, Duration = 10000 },
            [RequiredTagCarrierId] = new() { Id = RequiredTagCarrierId, Duration = 10000 },
            [CreatorExceptionCandidateId] = new() { Id = CreatorExceptionCandidateId, Duration = 10000 }
        });
        // tagged_buffs, both directions, exactly as SkillManager.Load fills them.
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>
        {
            [TaggedCandidateId] = [FreezeTagId],
            [CreatorExceptionCandidateId] = [FreezeTagId],
            [RequiredTagCarrierId] = [RequiredTagId]
        });
        SetField(skillManager, "_taggedBuffs", new Dictionary<uint, List<uint>>
        {
            [FreezeTagId] = [TaggedCandidateId, CreatorExceptionCandidateId],
            [RequiredTagId] = [RequiredTagCarrierId]
        });
        // tagged_immune_buffs: while the key buff is up, a candidate carrying the value tag is refused.
        SetField(skillManager, "_buffImmunityTags", new Dictionary<uint, List<uint>>
        {
            [ImmuneBuffId] = [FreezeTagId],
            [CreatorExceptionBuffId] = [FreezeTagId]
        });
        // tagged_require_buffs: the candidate needs the target to carry the value tag.
        SetField(skillManager, "_requiredBuffTags", new Dictionary<uint, List<uint>>
        {
            [RequiredTagCandidateId] = [RequiredTagId]
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

    [Test]
    public async Task Apply_TaggedCandidateWhileImmune_IsNotApplied()
    {
        var (owner, caster) = CreateUnits();
        ApplyImmuneBuff(owner, caster, ImmuneBuffId);

        ApplyBuffTemplate(caster, owner, TaggedCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(TaggedCandidateId)).IsFalse();
        // The immunity itself is still up and untouched.
        await Assert.That(owner.Buffs.CheckBuff(ImmuneBuffId)).IsTrue();
    }

    [Test]
    public async Task Apply_UntaggedCandidateWhileImmune_IsApplied()
    {
        var (owner, caster) = CreateUnits();
        ApplyImmuneBuff(owner, caster, ImmuneBuffId);

        ApplyBuffTemplate(caster, owner, UntaggedCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(UntaggedCandidateId)).IsTrue();
    }

    [Test]
    public async Task Apply_TaggedCandidateWithoutTheImmunity_IsApplied()
    {
        var (owner, caster) = CreateUnits();

        ApplyBuffTemplate(caster, owner, TaggedCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(TaggedCandidateId)).IsTrue();
    }

    [Test]
    public async Task Apply_TaggedCandidateThroughTheEffectPathWhileImmune_IsNotApplied()
    {
        var (owner, caster) = CreateUnits();
        ApplyImmuneBuff(owner, caster, ImmuneBuffId);

        ApplyBuffEffect(caster, owner, TaggedCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(TaggedCandidateId)).IsFalse();
    }

    [Test]
    public async Task Apply_UntaggedCandidateThroughTheEffectPathWhileImmune_IsApplied()
    {
        var (owner, caster) = CreateUnits();
        ApplyImmuneBuff(owner, caster, ImmuneBuffId);

        ApplyBuffEffect(caster, owner, UntaggedCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(UntaggedCandidateId)).IsTrue();
    }

    [Test]
    public async Task Apply_CandidateThatTheCreatorItselfApplies_PassesTheImmuneGrant()
    {
        var (owner, caster) = CreateUnits();
        // The granting buff is created by `caster`, so `immune_except_creator` lets it through.
        ApplyImmuneBuff(owner, caster, CreatorExceptionBuffId, creatorObjId: caster.ObjId);

        ApplyBuffTemplate(caster, owner, CreatorExceptionCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(CreatorExceptionCandidateId)).IsTrue();
    }

    [Test]
    public async Task Apply_CandidateThatSomebodyElseApplies_IsRefusedByTheCreatorExceptionGrant()
    {
        var (owner, caster) = CreateUnits();
        ApplyImmuneBuff(owner, caster, CreatorExceptionBuffId, creatorObjId: CasterObjId + 100);

        ApplyBuffTemplate(caster, owner, CreatorExceptionCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(CreatorExceptionCandidateId)).IsFalse();
    }

    [Test]
    public async Task Apply_CandidateWhoseRequiredTagIsMissing_IsNotApplied()
    {
        var (owner, caster) = CreateUnits();

        ApplyBuffTemplate(caster, owner, RequiredTagCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(RequiredTagCandidateId)).IsFalse();
    }

    [Test]
    public async Task Apply_CandidateWhoseRequiredTagIsPresent_IsApplied()
    {
        var (owner, caster) = CreateUnits();
        // 1454 무겁다 is the live carrier of tag 831; the buff id is faked but the tag is the DB's.
        ApplyImmuneBuff(owner, caster, RequiredTagCarrierId);

        ApplyBuffTemplate(caster, owner, RequiredTagCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(RequiredTagCandidateId)).IsTrue();
    }

    [Test]
    public async Task Apply_CandidateWhoseRequiredTagIsMissingThroughTheEffectPath_IsNotApplied()
    {
        var (owner, caster) = CreateUnits();

        ApplyBuffEffect(caster, owner, RequiredTagCandidateId);

        await Assert.That(owner.Buffs.CheckBuff(RequiredTagCandidateId)).IsFalse();
    }

    private static (BaseUnit Owner, Unit Caster) CreateUnits() =>
        (new BaseUnit { ObjId = OwnerObjId }, new Unit { ObjId = CasterObjId });

    /// <summary>
    /// Puts the immunity buff on the target the way <c>Buffs.AddBuff</c> does, with the given unit as
    /// the one that created it.
    /// </summary>
    private static void ApplyImmuneBuff(BaseUnit owner, Unit caster, uint buffId, uint? creatorObjId = null)
    {
        var creator = creatorObjId == null || creatorObjId == caster.ObjId
            ? caster
            : new Unit { ObjId = creatorObjId.Value };
        var template = SkillManager.Instance.GetBuffTemplate(buffId);

        owner.Buffs.AddBuff(new Buff(owner, creator, new SkillCasterUnit(creator.ObjId), template, null,
            DateTime.UtcNow)
        {
            // Keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test with no connection.
            Passive = true,
            AbLevel = 1
        });
    }

    private static void ApplyBuffTemplate(Unit caster, BaseUnit owner, uint buffId)
    {
        var template = SkillManager.Instance.GetBuffTemplate(buffId);
        template.Apply(caster, new SkillCasterUnit(caster.ObjId), owner, new SkillCastUnitTarget(owner.ObjId),
            new CastSkill(0, 1), new EffectSource(), new SkillObject(), DateTime.UtcNow);
    }

    private static void ApplyBuffEffect(Unit caster, BaseUnit owner, uint buffId)
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
