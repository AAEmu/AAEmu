using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// The immune hit result is a message about a cast, and it is sent once. A buff's own tick re-applies its
/// effects with a <see cref="CastBuff"/> action (<c>BuffTemplate.DoTick</c> / <c>DoAreaTick</c>), and a
/// buff trigger proc does the same, so an immune unit inside an aura or under a DoT used to send one
/// <see cref="SCUnitDamagedPacket"/> per tick to everyone nearby for the aura's whole life. A plot event
/// is a cast and keeps its message: <c>PlotEventEffect</c> hands its effects a <see cref="CastPlot"/>.
/// </summary>
[NotInParallel]
public class BuffImmuneBroadcastTests
{
    private const uint OwnerObjId = 1u;
    private const uint CasterObjId = 2u;
    private const uint AnyBuffId = 91001;
    private const uint InvincibleBuffId = 131; // 무적, the live carrier of melee_immune
    private const uint AnyPlotId = 620;
    private const ushort AnyTlId = 3;

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
            // Only the damage-immunity flag matters here; the packet agreement below compares the two
            // paths that refuse a unit, and this is the one DamageEffect checks.
            [InvincibleBuffId] = new() { Id = InvincibleBuffId, Duration = 0, MeleeImmune = true }
        });
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(skillManager, "_taggedBuffs", new Dictionary<uint, List<uint>>());
        SetField(skillManager, "_buffImmunityTags", new Dictionary<uint, List<uint>>());
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
    public async Task BroadcastBuffImmune_ForACast_SendsOneImmunePacket()
    {
        var owner = new RecordingUnit { ObjId = OwnerObjId };
        var caster = new Unit { ObjId = CasterObjId };

        owner.Buffs.BroadcastBuffImmune(caster, new CastSkill(0, 1), new SkillCasterUnit(caster.ObjId));

        await Assert.That(owner.Packets).HasCount().EqualTo(1);
        var packet = (SCUnitDamagedPacket)owner.Packets[0];
        // The same shape DamageEffect's CheckDamageImmune path sends: the immune hit type on 1 damage.
        await Assert.That(packet.HitType).IsEqualTo(SkillHitType.Immune);
    }

    [Test]
    public async Task BroadcastBuffImmune_ForAPlotCast_SendsOneImmunePacket()
    {
        // PlotEventEffect.cs:119 applies buff effects with a CastPlot, and 6 245 of the 38 043 skills carry
        // a plot id: a quiet refusal there is a refusal the player never sees.
        var owner = new RecordingUnit { ObjId = OwnerObjId };
        var caster = new Unit { ObjId = CasterObjId };

        owner.Buffs.BroadcastBuffImmune(caster, new CastPlot(AnyPlotId, AnyTlId, 1, 2),
            new SkillCasterUnit(caster.ObjId));

        await Assert.That(owner.Packets).HasCount().EqualTo(1);
        var packet = (SCUnitDamagedPacket)owner.Packets[0];
        await Assert.That(packet.HitType).IsEqualTo(SkillHitType.Immune);
    }

    [Test]
    public async Task BroadcastBuffImmune_ForABuffTickOrTrigger_StaysSilent()
    {
        var owner = new RecordingUnit { ObjId = OwnerObjId };
        var caster = new Unit { ObjId = CasterObjId };
        var buff = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId),
            new BuffTemplate { Id = AnyBuffId }, null, DateTime.UtcNow);

        owner.Buffs.BroadcastBuffImmune(caster, new CastBuff(buff), new SkillCasterUnit(caster.ObjId));

        await Assert.That(owner.Packets).IsEmpty();
    }

    [Test]
    public async Task BroadcastBuffImmune_SendsTheBodyDamageEffectSendsForDamageImmunity()
    {
        // The buff side and the damage side show the same hit result, so they have to agree on the body:
        // DamageEffect's CheckDamageImmune path sends damage 1 with SkillHitType.Immune (DamageEffect.cs:114)
        // and a refusal that carried something else would read as a different hit. Both packets are built
        // from the same cast and caster objects, so equal bytes are the whole agreement.
        var owner = new RecordingUnit { ObjId = OwnerObjId, Hp = 100 };
        var caster = new RecordingUnit { ObjId = CasterObjId, Hp = 100 };
        var casterObj = new SkillCasterUnit(caster.ObjId);
        var castObj = new CastSkill(0, 1);
        owner.Buffs.AddBuff(new Buff(owner, caster, casterObj,
            SkillManager.Instance.GetBuffTemplate(InvincibleBuffId), null, DateTime.UtcNow) { Passive = true });

        new DamageEffect { DamageType = DamageType.Melee }.Apply(caster, casterObj, owner,
            new SkillCastUnitTarget(owner.ObjId), castObj, new EffectSource(), new SkillObject(), DateTime.UtcNow);
        owner.Buffs.BroadcastBuffImmune(caster, castObj, casterObj);

        await Assert.That(owner.Packets).HasCount().EqualTo(2);
        await Assert.That(Bytes(owner.Packets[1])).IsEquivalentTo(Bytes(owner.Packets[0]));
    }

    private static byte[] Bytes(GamePacket packet)
    {
        var stream = new PacketStream();
        packet.Write(stream);
        return stream.GetBytes();
    }

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private sealed class RecordingUnit : Unit
    {
        public List<GamePacket> Packets { get; } = [];

        public override void BroadcastPacket(GamePacket packet, bool self) => Packets.Add(packet);
    }
}
