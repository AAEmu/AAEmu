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
/// The <c>save_pos</c> half of <c>Buffs.AddBuff</c>: the point that move_to_saved_pos (special type 172)
/// sends the caster back to.
/// </summary>
/// <remarks>
/// content: <c>buffs.save_pos</c> is 't' on 8 rows and <c>special_effects</c> type 172 names one of them in
/// value1 on each of its 9 rows (24610 via 41487/45779, 24947 via 41964/45772/45778, 19037 via 42012), so
/// the capture is the only thing that makes a recall land anywhere. The buffs and ids are faked here; the
/// shape is the live one.
/// </remarks>
[NotInParallel]
public class BuffsSavedPositionTests
{
    private const uint MarkingBuffId = 92001;
    private const uint PlainBuffId = 92002;
    private const uint SavedZoneId = 186;

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
        // Neither family carries a tag, so AddBuff never reaches a tolerance lookup; the empty dictionary
        // is what an untagged buff resolves to.
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(skillManager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [MarkingBuffId] = new BuffTemplate { Id = MarkingBuffId, Duration = 5000, SavePos = true },
            [PlainBuffId] = new BuffTemplate { Id = PlainBuffId, Duration = 5000 }
        });
        _skillManagerField.SetValue(null, skillManager);

        // AddBuff applies the buff's unit modifiers through BuffGameData, which an unseeded singleton
        // would dereference as null.
        var buffGameData = new BuffGameData();
        SetField(buffGameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(buffGameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(buffGameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        _buffGameDataField.SetValue(null, buffGameData);

        // A timed buff schedules its own expiry, which must not reach a real scheduler.
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
    public async Task AddBuff_OnASavePosFamily_CapturesWhereTheOwnerStands()
    {
        var (owner, caster) = CreateUnits();
        owner.Transform.Local.Position = new System.Numerics.Vector3(1234.5f, 6789.25f, 42.5f);
        owner.Transform.Local.Rotation = new System.Numerics.Vector3(0f, 0f, 1.5f);

        var buff = CreateBuff(owner, caster, MarkingBuffId);
        owner.Buffs.AddBuff(buff);

        await Assert.That(buff.SavedPosition).IsNotNull();
        var saved = buff.SavedPosition.Value;
        await Assert.That(saved.X).IsEqualTo(1234.5f);
        await Assert.That(saved.Y).IsEqualTo(6789.25f);
        await Assert.That(saved.Z).IsEqualTo(42.5f);
        await Assert.That(saved.YawRad).IsEqualTo(1.5f);
        await Assert.That(saved.ZoneId).IsEqualTo(SavedZoneId);
    }

    [Test]
    public async Task AddBuff_OnAnyOtherFamily_LeavesThePointUnset()
    {
        // The flag is what decides, not the buff id: the other 9,934 rows must not pay for a marker the
        // recall effect will never look for.
        var (owner, caster) = CreateUnits();

        var buff = CreateBuff(owner, caster, PlainBuffId);
        owner.Buffs.AddBuff(buff);

        await Assert.That(buff.SavedPosition).IsNull();
    }

    [Test]
    public async Task TheCapturedPoint_IsTheOneTheRecallGuardAccepts()
    {
        var (owner, caster) = CreateUnits();
        owner.Transform.Local.Position = new System.Numerics.Vector3(100f, 200f, 30f);

        var buff = CreateBuff(owner, caster, MarkingBuffId);
        owner.Buffs.AddBuff(buff);

        // What move_to_saved_pos asks: is this point still in the zone and instance the caster is in?
        await Assert.That(SavedPositionRules.CanReturnTo(buff.SavedPosition, SavedZoneId,
            owner.Transform.InstanceId)).IsTrue();
        await Assert.That(SavedPositionRules.CanReturnTo(buff.SavedPosition, SavedZoneId + 1,
            owner.Transform.InstanceId)).IsFalse();
    }

    private static (BaseUnit Owner, BaseUnit Caster) CreateUnits()
    {
        var owner = new BaseUnit { ObjId = 1 };
        // KeepZoneQuietly assigns the zone key without raising OnZoneChange, which would reach for the
        // world manager. The zone is only ever read back here.
        owner.Transform.KeepZoneQuietly(SavedZoneId);
        return (owner, new BaseUnit { ObjId = 2 });
    }

    private static Buff CreateBuff(BaseUnit owner, BaseUnit caster, uint buffId) =>
        new(owner, caster, new SkillCasterUnit(caster.ObjId),
            SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow)
        {
            // Keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test with no connection.
            Passive = true,
            AbLevel = 1
        };

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
