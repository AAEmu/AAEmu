using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The cast target an effect receives. A summon cast (skill 15802, target SummonPos) arrives as a position
/// packet carrying the heading the client picked; the effect used to get a unit target instead, so every
/// hull was planted facing north.
/// </summary>
[NotInParallel]
public class SkillEffectCastTargetTests
{
    private const uint SummonVehicleSkill = 15802;
    private const float ClientHeading = 3.154f;

    private FieldInfo _skillManagerSingletonField;
    private object _previousSkillManager;

    [Before(Test)]
    public void SetupSkillManager()
    {
        _skillManagerSingletonField = typeof(Singleton<SkillManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousSkillManager = _skillManagerSingletonField.GetValue(null);
        _skillManagerSingletonField.SetValue(null, new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object));
    }

    [After(Test)]
    public void RestoreSkillManager()
    {
        _skillManagerSingletonField.SetValue(null, _previousSkillManager);
    }

    [Test]
    public async Task SummonPositionCast_EffectOnTheStandGetsTheClientPacketAndHeading()
    {
        var capture = new CapturingSpecialEffect { SpecialEffectTypeId = SpecialType.SpawnSlave };
        var skill = new Skill(new SkillTemplate
        {
            Id = SummonVehicleSkill,
            Effects = [new SkillEffect { Template = capture, ApplicationMethod = SkillEffectApplicationMethod.Target, Chance = 100 }]
        });
        var caster = new Unit { ObjId = 899 };
        // The stand SetInitialTarget builds for a SummonPos cast.
        var stand = new BaseUnit { ObjId = uint.MaxValue };
        var packet = new SkillCastPositionTarget
        {
            Type = SkillCastTargetType.Position,
            PosX = 15747.6f,
            PosY = 15400.6f,
            PosZ = 100f,
            PosRot = ClientHeading
        };

        skill.ApplyEffects(caster, new SkillCasterUnit(caster.ObjId), stand, packet, null);

        // SpawnSlave reads the stand position and the packet heading.
        await Assert.That(SlaveSummonSeedRules.TryReadWorldSeed(
                capture.TargetObj, 15747.6f, 15400.6f, 100f, out _, out _, out _, out var yaw))
            .IsTrue();
        await Assert.That(yaw).IsEqualTo(ClientHeading);
        await Assert.That(capture.Target).IsSameReferenceAs(stand);
        await Assert.That(capture.TargetObj).IsSameReferenceAs(packet);
    }

    [Test]
    public async Task StandOfAnyPositionPacket_KeepsThePacket()
    {
        var stand = new BaseUnit { ObjId = uint.MaxValue };
        SkillCastTarget[] packets =
        [
            new SkillCastPositionTarget { PosRot = ClientHeading },
            new SkillCastPosition2Target { PosX = 1f },
            new SkillCastPosition3Target { Pitch = 0.5f }
        ];

        foreach (var packet in packets)
            await Assert.That(Skill.EffectCastTarget(stand, packet)).IsSameReferenceAs(packet);
    }

    [Test]
    public async Task UnitNamedByThePacket_KeepsThePacket()
    {
        var packet = new SkillCastUnitTarget(42);

        await Assert.That(Skill.EffectCastTarget(new Unit { ObjId = 42 }, packet)).IsSameReferenceAs(packet);
    }

    [Test]
    public async Task OtherUnitsOfAPositionCast_GetAUnitTarget()
    {
        // An area hit around the stand, and the caster origin a ground cast falls back to, are units.
        var packet = new SkillCastPositionTarget { PosRot = ClientHeading };

        var areaHit = Skill.EffectCastTarget(new Unit { ObjId = 7 }, packet);
        var origin = Skill.EffectCastTarget(new Unit { ObjId = 899 }, packet);

        await Assert.That(areaHit).IsTypeOf<SkillCastUnitTarget>();
        await Assert.That(areaHit.ObjId).IsEqualTo(7u);
        await Assert.That(origin).IsTypeOf<SkillCastUnitTarget>();
        await Assert.That(origin.ObjId).IsEqualTo(899u);
    }

    [Test]
    public async Task StandOfAUnitPacket_GetsAUnitTarget()
    {
        // Pos-type skills build a stand even when the packet named a unit; that packet has no position.
        var result = Skill.EffectCastTarget(new BaseUnit { ObjId = uint.MaxValue }, new SkillCastUnitTarget(5));

        await Assert.That(result).IsTypeOf<SkillCastUnitTarget>();
        await Assert.That(result.ObjId).IsEqualTo(uint.MaxValue);
    }

    private sealed class CapturingSpecialEffect : SpecialEffect
    {
        public BaseUnit Target { get; private set; }
        public SkillCastTarget TargetObj { get; private set; }

        public override void Apply(
            BaseUnit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source,
            SkillObject skillObject,
            DateTime time,
            CompressedGamePackets packetBuilder = null)
        {
            Target = target;
            TargetObj = targetObj;
        }
    }
}
