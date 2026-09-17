using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The disabling-state rules behind E9e: what a rooted, stunned, sleeping or silenced unit may do.
/// </summary>
/// <remarks>
/// The membership of the two sets is what matters here. Movement and casting do not share it — a root
/// stops the walk and leaves the cast alone, a silence does the opposite — and both have to stay equal
/// to what <c>Buffs.AddBuff</c> already does with the same flags (interrupt on stun/silence/sleep,
/// dismount on stun/sleep/root), or the server would interrupt a cast it then lets restart.
/// </remarks>
[NotInParallel]
public class CrowdControlRulesTests
{
    private const uint RootedBuffId = 1758;
    private const uint SilencedBuffId = 2675;
    private const uint SleepingBuffId = 5695;
    private const uint StunnedBuffId = 31544;
    private const uint InertBuffId = 23244;

    private SingletonScope<BuffGameData> _buffGameData;
    private SingletonScope<SkillManager> _skills;

    [Before(Test)]
    public void InstallContentLookups()
    {
        // Adding a buff reads buff_modifiers (BuffGameData) and the buff's own template plus its tags
        // (SkillManager). With no content loaded they have to come back empty rather than from a null
        // table — Buffs.AddBuff dereferences the template it gets back for the buff id.
        var gameData = new BuffGameData();
        SetField(gameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(gameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(gameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        _buffGameData = new SingletonScope<BuffGameData>(gameData);

        var skillManager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(skillManager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [RootedBuffId] = new BuffTemplate { Id = RootedBuffId, Duration = 0, Root = true },
            [SilencedBuffId] = new BuffTemplate { Id = SilencedBuffId, Duration = 0, Silence = true },
            [SleepingBuffId] = new BuffTemplate { Id = SleepingBuffId, Duration = 0, Sleep = true },
            [StunnedBuffId] = new BuffTemplate { Id = StunnedBuffId, Duration = 0, Stun = true },
            [InertBuffId] = new BuffTemplate { Id = InertBuffId, Duration = 0 }
        });
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(skillManager, "_taggedBuffs", new Dictionary<uint, List<uint>>());
        _skills = new SingletonScope<SkillManager>(skillManager);
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        _skills.Dispose();
        _buffGameData.Dispose();
    }

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static CrowdControlRules.EnforcedState State(
        bool stunned = false,
        bool asleep = false,
        bool silenced = false,
        bool rooted = false)
        => new(stunned, asleep, silenced, rooted);

    private static Unit UnitWithBuff(BuffTemplate template)
    {
        var owner = new Unit { ObjId = 1 };
        var caster = new Unit { ObjId = 2 };
        owner.Buffs.AddBuff(new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), template, null, DateTime.UtcNow)
        {
            Passive = true,
            AbLevel = 1
        });

        return owner;
    }

    [Test]
    public async Task BlocksMovement_RootStunAndSleepStopTheWalk()
    {
        // All three are the flags Buffs.AddBuff dismounts a rider on, and the client takes the controls
        // away for all three.
        await Assert.That(CrowdControlRules.BlocksMovement(State(rooted: true))).IsTrue();
        await Assert.That(CrowdControlRules.BlocksMovement(State(stunned: true))).IsTrue();
        await Assert.That(CrowdControlRules.BlocksMovement(State(asleep: true))).IsTrue();
    }

    [Test]
    public async Task BlocksMovement_ASilenceLeavesTheWalkAlone()
    {
        // 198 shipped buffs are silence-only. Blocking movement on them would root the caster as well.
        await Assert.That(CrowdControlRules.BlocksMovement(State(silenced: true))).IsFalse();
    }

    [Test]
    public async Task BlocksMovement_NoFlagsIsExactlyTheOldPath()
    {
        // The neutrality pin: a unit carrying no disabling buff answers "no" to the gate, so its move
        // packet takes the same branch it took before the gate existed.
        await Assert.That(CrowdControlRules.BlocksMovement(CrowdControlRules.EnforcedState.None)).IsFalse();
        await Assert.That(CrowdControlRules.ReadState(new Unit { ObjId = 1 })).IsEqualTo(CrowdControlRules.EnforcedState.None);
    }

    [Test]
    public async Task RejectCast_SilenceStunAndSleepStopTheCast()
    {
        var silenced = CrowdControlRules.RejectCast(State(silenced: true));
        var stunned = CrowdControlRules.RejectCast(State(stunned: true));
        var asleep = CrowdControlRules.RejectCast(State(asleep: true));

        await Assert.That(silenced.HasValue).IsTrue();
        await Assert.That(silenced.Value).IsEqualTo(SkillResult.Silence);
        await Assert.That(stunned.HasValue).IsTrue();
        await Assert.That(stunned.Value).IsEqualTo(SkillResult.CannotCastInStun);
        await Assert.That(asleep.HasValue).IsTrue();
        await Assert.That(asleep.Value).IsEqualTo(SkillResult.CannotCastInStun);
    }

    [Test]
    public async Task RejectCast_ARootLeavesTheCastAlone()
    {
        // 833 shipped buffs carry root. Crippled is the same kind of state and is not in this set either:
        // a rooted character still casts, which is what the D3 condition bits say as well.
        await Assert.That(CrowdControlRules.RejectCast(State(rooted: true))).IsNull();
    }

    [Test]
    public async Task RejectCast_StunOutranksSilenceInTheReportedResult()
    {
        // Both states on, silence-permitting: the refusal has to name the stun the client is drawing.
        // (stunned allowed, asleep allowed, silenced allowed)
        var result = CrowdControlRules.RejectCast(State(stunned: true, silenced: true), false, true, true);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(SkillResult.CannotCastInStun);
    }

    [Test]
    public async Task RejectCast_HonoursTheSkillsOwnPermissionBits()
    {
        // 강인한 의지, 활력 방패 and 기선 제압 carry use_condition_bits 20513 - stun AND sleep AND silence
        // together - which can only mean "usable in that state". Those bits have to survive this gate or
        // the one skill that exists to break out of a stun becomes uncastable during one.
        await Assert.That(CrowdControlRules.RejectCast(State(stunned: true), true, false, false)).IsNull();
        await Assert.That(CrowdControlRules.RejectCast(State(asleep: true), false, true, false)).IsNull();
        await Assert.That(CrowdControlRules.RejectCast(State(silenced: true), false, false, true)).IsNull();

        // A permission for one state is not a permission for the others.
        var silenced = CrowdControlRules.RejectCast(State(silenced: true), true, true, false);
        var stunned = CrowdControlRules.RejectCast(State(stunned: true), false, true, true);

        await Assert.That(silenced.HasValue).IsTrue();
        await Assert.That(silenced.Value).IsEqualTo(SkillResult.Silence);
        await Assert.That(stunned.HasValue).IsTrue();
        await Assert.That(stunned.Value).IsEqualTo(SkillResult.CannotCastInStun);
    }

    [Test]
    public async Task RejectCast_NoFlagsIsExactlyTheOldPath()
    {
        await Assert.That(CrowdControlRules.RejectCast(CrowdControlRules.EnforcedState.None)).IsNull();
        await Assert.That(CrowdControlRules.RejectCast(CrowdControlRules.EnforcedState.None, false, false, false)).IsNull();
        await Assert.That(CrowdControlRules.RejectCast(CrowdControlRules.EnforcedState.None, SkillResult.NoTarget))
            .IsEqualTo(SkillResult.NoTarget);
    }

    [Test]
    public async Task ReadState_ReadsTheFlagsOffTheUnitsLiveBuffs()
    {
        await Assert.That(CrowdControlRules.ReadState(
                UnitWithBuff(new BuffTemplate { Id = RootedBuffId, Duration = 0, Root = true })))
            .IsEqualTo(State(rooted: true));
    }

    [Test]
    public async Task ReadState_IgnoresBuffsThatCarryNoneOfTheFlags()
    {
        // 30k-odd shipped buffs carry no disabling flag at all; they must read as the neutral state.
        var unit = UnitWithBuff(new BuffTemplate { Id = InertBuffId, Duration = 0 });

        await Assert.That(CrowdControlRules.ReadState(unit)).IsEqualTo(CrowdControlRules.EnforcedState.None);
        await Assert.That(CrowdControlRules.BlocksMovement(CrowdControlRules.ReadState(unit))).IsFalse();
        await Assert.That(CrowdControlRules.RejectCast(CrowdControlRules.ReadState(unit))).IsNull();
    }

    [Test]
    public async Task ReadState_ComposesEveryFlagOnTheUnit()
    {
        var unit = UnitWithBuff(new BuffTemplate { Id = RootedBuffId, Duration = 0, Root = true });
        var caster = new Unit { ObjId = 2 };
        foreach (var template in new[]
                 {
                     new BuffTemplate { Id = SilencedBuffId, Duration = 0, Silence = true },
                     new BuffTemplate { Id = SleepingBuffId, Duration = 0, Sleep = true },
                     new BuffTemplate { Id = StunnedBuffId, Duration = 0, Stun = true }
                 })
        {
            unit.Buffs.AddBuff(new Buff(unit, caster, new SkillCasterUnit(caster.ObjId), template, null, DateTime.UtcNow)
            {
                Passive = true,
                AbLevel = 1
            });
        }

        var state = CrowdControlRules.ReadState(unit);
        var refusal = CrowdControlRules.RejectCast(state);

        await Assert.That(state).IsEqualTo(State(stunned: true, asleep: true, silenced: true, rooted: true));
        await Assert.That(CrowdControlRules.BlocksMovement(state)).IsTrue();
        await Assert.That(refusal.HasValue).IsTrue();
        await Assert.That(refusal.Value).IsEqualTo(SkillResult.CannotCastInStun);
    }
}
