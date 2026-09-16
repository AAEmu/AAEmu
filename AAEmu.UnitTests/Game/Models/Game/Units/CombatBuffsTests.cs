using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// The combat_buffs path end to end: AddCombatBuffs indexes a row under every hit type its mask sets,
/// TriggerCombatBuffs fires it only on the side of the hit the row names, and removing the required
/// buff takes the row out of every index again.
/// </summary>
/// <remarks>
/// content: game_decrypted.sqlite3 combat_buffs 8 (req buff 494 신명 → buff 15247 신명 발동,
/// hit_type_bits 17412 = the three criticals, buff_from_source/buff_to_source 't', is_heal_spell 'f'),
/// 10 (req 484 원거리 방어 → 506, 65536 = ranged_block, 'f'/'f'), 138 (req 22266 독 바르기 → 22271,
/// 591205 = the physical hit/critical/block/parry set, 't'/'t', reverse_target_on 't'), 141 (req 7563
/// 사신의 손길 → 22542, hit_skill_tag_id 4751, 20480 = spell_hit|spell_critical) and 126 (req 2926
/// 낙인 생성 → 15002, hit_skill_id 10434, 20480). Every id below is one of those rows; the tag on
/// skill 11441 comes from tagged_skills, and 39661 (tag 4749, not 4751) stands in for a skill without
/// it.
/// </remarks>
[NotInParallel]
public class CombatBuffsTests
{
    private const uint ReqBuffId = 494;
    private const uint AppliedBuffId = 15247;
    private const uint AnyCriticalBits = 17412;
    private const uint ReqBuffRangedBlockId = 484;
    private const uint AppliedBuffRangedBlockId = 506;
    private const uint RangedBlockBits = 65536;
    private const uint ReqBuffTaggedId = 7563;
    private const uint AppliedBuffTaggedId = 22542;
    private const uint SpellHitBits = 20480;
    private const uint HitSkillTagId = 4751;
    private const uint TaggedSkillId = 11441;
    private const uint UntaggedSkillId = 39661;
    private const uint ReqBuffHitSkillId = 2926;
    private const uint AppliedBuffHitSkillId = 15002;
    private const uint RequiredHitSkillId = 10434;
    private const uint OtherHitSkillId = 40786;
    private const uint ReqBuffPoisonId = 22266;
    private const uint AppliedBuffPoisonId = 22271;
    private const uint PhysicalBits = 591205;
    private const uint UnnamedBitsReqBuffId = 91001;
    private const uint UnnamedBitsAppliedBuffId = 91002;
    private const uint UnnamedBits = 1u << 1; // enum_skill_hit_type has no id 2, so this bit names nothing

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
            [ReqBuffId] = Buff(ReqBuffId),
            [AppliedBuffId] = Buff(AppliedBuffId),
            [ReqBuffRangedBlockId] = Buff(ReqBuffRangedBlockId),
            [AppliedBuffRangedBlockId] = Buff(AppliedBuffRangedBlockId),
            [ReqBuffTaggedId] = Buff(ReqBuffTaggedId),
            [AppliedBuffTaggedId] = Buff(AppliedBuffTaggedId),
            [ReqBuffHitSkillId] = Buff(ReqBuffHitSkillId),
            [AppliedBuffHitSkillId] = Buff(AppliedBuffHitSkillId),
            [ReqBuffPoisonId] = Buff(ReqBuffPoisonId),
            [AppliedBuffPoisonId] = Buff(AppliedBuffPoisonId),
            [UnnamedBitsReqBuffId] = Buff(UnnamedBitsReqBuffId),
            [UnnamedBitsAppliedBuffId] = Buff(UnnamedBitsAppliedBuffId)
        });
        SetField(skillManager, "_combatBuffs", new Dictionary<uint, List<CombatBuffTemplate>>
        {
            [ReqBuffId] =
            [
                CombatBuff(8, AppliedBuffId, AnyCriticalBits)
            ],
            [ReqBuffRangedBlockId] =
            [
                CombatBuff(10, AppliedBuffRangedBlockId, RangedBlockBits, buffFromSource: false, buffToSource: false)
            ],
            [ReqBuffTaggedId] =
            [
                CombatBuff(141, AppliedBuffTaggedId, SpellHitBits, hitSkillTagId: HitSkillTagId)
            ],
            [ReqBuffHitSkillId] =
            [
                CombatBuff(126, AppliedBuffHitSkillId, SpellHitBits, hitSkillId: RequiredHitSkillId)
            ],
            [ReqBuffPoisonId] =
            [
                CombatBuff(138, AppliedBuffPoisonId, PhysicalBits, reverseTargetOn: true)
            ],
            [UnnamedBitsReqBuffId] =
            [
                CombatBuff(91001, UnnamedBitsAppliedBuffId, UnnamedBits)
            ]
        });
        SetField(skillManager, "_skillTags", new Dictionary<uint, List<uint>>
        {
            [TaggedSkillId] = [HitSkillTagId],
            [UntaggedSkillId] = [4749]
        });
        _skillManagerField.SetValue(null, skillManager);

        // AddBuff reads modifiers and tolerances from BuffGameData and schedules through the task
        // managers; none of that is under test here, so they stay empty.
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
    public async Task TriggerCombatBuffs_AnyCriticalRow_FiresFromTwoHitTypesInTheMaskAndNotOneOutside()
    {
        var (attacker, victim) = CreateUnits();
        attacker.Buffs.AddBuff(BuffOn(attacker, attacker, ReqBuffId));

        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.MeleeCritical, false);

        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffId)).IsTrue();
        await Assert.That(victim.Buffs.CheckBuff(AppliedBuffId)).IsFalse();

        attacker.Buffs.RemoveBuff(AppliedBuffId, notifyZone: false);
        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.RangedCritical, false);
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffId)).IsTrue();

        // MeleeHit is bit 0 and is not one of the three criticals 17412 sets.
        attacker.Buffs.RemoveBuff(AppliedBuffId, notifyZone: false);
        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.MeleeHit, false);
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffId)).IsFalse();

        // SpellCritical is in the mask, but row 8 is is_heal_spell 'f', so a critical heal must not fire it.
        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.SpellCritical, true);
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffId)).IsFalse();
    }

    [Test]
    public async Task TriggerCombatBuffs_RowOnTheReceiverSide_FiresOnlyForTheUnitThatTookTheHit()
    {
        var (attacker, victim) = CreateUnits();
        attacker.Buffs.AddBuff(BuffOn(attacker, attacker, ReqBuffRangedBlockId));
        victim.Buffs.AddBuff(BuffOn(victim, attacker, ReqBuffRangedBlockId));

        // combat_buffs 10 is buff_to_source 'f': the entry only fires in the list of the unit that was
        // hit, so the identical entry on the attacker must stay quiet.
        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.RangedBlock, false);
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffRangedBlockId)).IsFalse();
        await Assert.That(victim.Buffs.CheckBuff(AppliedBuffRangedBlockId)).IsFalse();

        victim.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.RangedBlock, false);
        await Assert.That(victim.Buffs.CheckBuff(AppliedBuffRangedBlockId)).IsTrue();
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffRangedBlockId)).IsFalse();
    }

    [Test]
    public async Task TriggerCombatBuffs_ReverseTargetOn_BuffsTheOtherCombatant()
    {
        var (attacker, victim) = CreateUnits();
        attacker.Buffs.AddBuff(BuffOn(attacker, attacker, ReqBuffPoisonId));

        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.MeleeHit, false);

        // combat_buffs 138 (독 바르기): the poison lands on the victim, not on the unit carrying the entry.
        await Assert.That(victim.Buffs.CheckBuff(AppliedBuffPoisonId)).IsTrue();
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffPoisonId)).IsFalse();
        // …and it is still cast by the unit that owns the entry, so the victim's own modifiers (and not
        // the attacker's) are what Buffs.AddBuff scales the duration with.
        await Assert.That(victim.Buffs.GetEffectFromBuffId(AppliedBuffPoisonId).Caster).IsSameReferenceAs(attacker);
    }

    [Test]
    public async Task TriggerCombatBuffs_HitSkillTag_MatchesOnlyASkillCarryingTheTag()
    {
        var (attacker, victim) = CreateUnits();
        attacker.Buffs.AddBuff(BuffOn(attacker, attacker, ReqBuffTaggedId));

        // A hit that cannot be attributed to a skill (a buff tick) does not satisfy the row.
        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.SpellCritical, false);
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffTaggedId)).IsFalse();

        // Skills with other tags do not either: 39661 carries 4749, the row wants 4751.
        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.SpellCritical, false,
            SkillOf(UntaggedSkillId));
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffTaggedId)).IsFalse();

        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.SpellCritical, false,
            SkillOf(TaggedSkillId));
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffTaggedId)).IsTrue();
    }

    [Test]
    public async Task TriggerCombatBuffs_HitSkillId_MatchesOnlyTheSkillThatLanded()
    {
        var (attacker, victim) = CreateUnits();
        attacker.Buffs.AddBuff(BuffOn(attacker, attacker, ReqBuffHitSkillId));

        // combat_buffs 126 wants hit_skill_id 10434 (원혼 소환) to have landed.
        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.SpellHit, false,
            SkillOf(OtherHitSkillId));
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffHitSkillId)).IsFalse();

        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.SpellHit, false,
            SkillOf(RequiredHitSkillId));
        await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffHitSkillId)).IsTrue();
    }

    [Test]
    public async Task RemoveCombatBuff_ThroughTheRequiredBuff_LeavesNoTriggerInAnyIndex()
    {
        var (attacker, victim) = CreateUnits();
        attacker.Buffs.AddBuff(BuffOn(attacker, attacker, ReqBuffId));

        var maskedHitTypes = new[]
        {
            SkillHitType.MeleeCritical, SkillHitType.RangedCritical, SkillHitType.SpellCritical
        };

        foreach (var hitType in maskedHitTypes)
        {
            attacker.Buffs.RemoveBuff(AppliedBuffId, notifyZone: false);
            attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, hitType, false);
            await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffId)).IsTrue();
        }

        // 17412 sets three hit types, so the entry sits in three buckets; losing the required buff has
        // to clear all of them. The applied buff from the loop above goes first.
        attacker.Buffs.RemoveBuff(ReqBuffId, notifyZone: false);
        attacker.Buffs.RemoveBuff(AppliedBuffId, notifyZone: false);

        foreach (var hitType in maskedHitTypes)
        {
            attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, hitType, false);
            await Assert.That(attacker.Buffs.CheckBuff(AppliedBuffId)).IsFalse();
        }

        foreach (var bucket in ReadBuckets(attacker).Values)
            await Assert.That(bucket).IsEmpty();
    }

    [Test]
    public async Task AddCombatBuffs_MaskThatNamesNoHitType_IsNotRegisteredAtAll()
    {
        var (attacker, victim) = CreateUnits();
        attacker.Buffs.AddBuff(BuffOn(attacker, attacker, UnnamedBitsReqBuffId));

        // The loader logs and drops such rows; if one reaches a unit it must still not be indexed, and
        // never under Invalid.
        await Assert.That(ReadBuckets(attacker).ContainsKey(SkillHitType.Invalid)).IsFalse();
        foreach (var bucket in ReadBuckets(attacker).Values)
            await Assert.That(bucket).IsEmpty();

        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.Invalid, false);
        attacker.CombatBuffs.TriggerCombatBuffs(attacker, victim, SkillHitType.MeleeHit, false);
        await Assert.That(attacker.Buffs.CheckBuff(UnnamedBitsAppliedBuffId)).IsFalse();
    }

    private static CombatBuffTemplate CombatBuff(uint id, uint buffId, uint hitTypeBits,
        bool buffFromSource = true, bool buffToSource = true, bool reverseTargetOn = false,
        uint hitSkillId = 0, uint hitSkillTagId = 0) =>
        new()
        {
            Id = id,
            BuffId = buffId,
            HitTypeBits = hitTypeBits,
            BuffFromSource = buffFromSource,
            BuffToSource = buffToSource,
            ReverseTargetOn = reverseTargetOn,
            HitSkillId = hitSkillId,
            HitSkillTagId = hitSkillTagId,
            IsHealSpell = false
        };

    private static BuffTemplate Buff(uint id) => new() { Id = id, Duration = 5000 };

    // TriggerCombatBuffs needs real Units: the Buff it creates takes the owner as IBaseUnit but the
    // caster as Unit, so a plain BaseUnit is refused at the top of the trigger.
    private static (Unit Attacker, Unit Victim) CreateUnits() =>
        (new Unit { ObjId = 1 }, new Unit { ObjId = 2 });

    private static Buff BuffOn(BaseUnit owner, BaseUnit caster, uint buffId) =>
        new(owner, caster, new SkillCasterUnit(caster.ObjId),
            SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow)
        {
            // Keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test with no connection.
            Passive = true,
            AbLevel = 1
        };

    private static Skill SkillOf(uint skillId) => new() { Template = new SkillTemplate { Id = skillId } };

    private static Dictionary<SkillHitType, List<CombatBuffTemplate>> ReadBuckets(BaseUnit unit) =>
        (Dictionary<SkillHitType, List<CombatBuffTemplate>>)typeof(CombatBuffs)
            .GetField("_cbuffsByHitType", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(unit.CombatBuffs);

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
