using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The two controller kinds this batch added Ã¢â‚¬â€ wandering (3, the fear shape) and floating (1, the lift) Ã¢â‚¬â€ and
/// the plot path into them: a plot effect of type SkillController used to log and do nothing, of which
/// 10.0.2.13 has 2,010 rows.
/// </summary>
[NotInParallel]
public class RemainingSkillControllerTests
{
    private SingletonScope<WorldManager> _worldManager;
    private SingletonScope<SusManager> _susManager;
    private SingletonScope<SkillManager> _skillManager;

    [Before(HookType.Test)]
    public void InstallMovementLookups()
    {
        _skillManager = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());
        _worldManager = new SingletonScope<WorldManager>(new WorldManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object)));
        _susManager = new SingletonScope<SusManager>(new SusManager(Mock.Of<IWorldManager>().Object));
    }

    [After(HookType.Test)]
    public void RestoreMovementLookups()
    {
        _susManager?.Dispose();
        _worldManager?.Dispose();
        _skillManager?.Dispose();
    }

    /// <summary>skill_controllers 3495 shape: a wandering row, value1 1500 ms.</summary>
    private static SkillControllerTemplate WanderingRow(int durationMs = 1500) => new()
    {
        Id = 3495,
        KindId = 3,
        Value = [durationMs, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0]
    };

    /// <summary>skill_controllers 314 shape: a floating row, value1 1700 ms and value2 12000.</summary>
    private static SkillControllerTemplate FloatingRow(int liftMs = 1700, int heightMm = 12000) => new()
    {
        Id = 314,
        KindId = 1,
        Value = [liftMs, heightMm, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0]
    };

    /// <summary>An NPC that keeps every packet it broadcasts, so a test can see the movement it emits.</summary>
    private sealed class RecordingNpc : AAEmu.Game.Models.Game.NPChar.Npc
    {
        public List<GamePacket> Packets { get; } = [];

        public override void BroadcastPacket(GamePacket packet, bool self) => Packets.Add(packet);
    }

    private static RecordingNpc FearingNpc()
    {
        var npc = new RecordingNpc
        {
            ObjId = 300, Level = 50, Hp = 1000, MaxHp = 1000, DisabledSetPosition = true
        };
        npc.Transform.Local.SetPosition(10f, 10f, 0f);
        return npc;
    }

    [Test]
    public async Task AFearSkillMakesAnNpcWanderAndEmitMovement()
    {
        var npc = FearingNpc();
        var caster = new Unit { ObjId = 400, Hp = 1000, MaxHp = 1000 };

        // value1 is the only column this test needs the row for. The controller arms its release from the
        // wall clock in its constructor, so the wandering is asked for long enough that nothing between
        // here and the ticks can spend it: a fear whose time is up ends on the first tick and the victim
        // never moves at all, which is its own test below, where the time is set rather than waited out.
        const int durationMs = 60_000;
        var controller = (WanderingSkillController)SkillController.CreateSkillController(WanderingRow(durationMs), npc, caster);
        await Assert.That(controller).IsNotNull();
        await Assert.That(controller.Owner).IsSameReferenceAs(npc);
        await Assert.That(controller.Duration).IsEqualTo(durationMs);

        npc.ActiveSkillController = controller;
        var start = npc.Transform.Local.ClonePosition();

        // Two of the hundred-millisecond ticks the controller subscribes with: each leg is 2 m long, so the
        // first tick reaches the point the victim was running to and the second one picks a new heading.
        //
        // How far the victim ends up from where it started is not a number this test can state. The
        // heading is re-picked at random for every leg, so two legs whose headings nearly cancel put it
        // back where it began - four metres of travel read as no movement at all, which is exactly what
        // measuring the end-to-end offset used to do. What is the same on every draw is the leg itself:
        // the controller aims each leg LegLength ahead, so a tick has to carry the owner exactly that far,
        // and the second leg has to be a full leg too rather than the controller stopping at the first.
        controller.Tick(TimeSpan.FromSeconds(1));
        var afterFirstLeg = npc.Transform.Local.ClonePosition();
        var firstLeg = MathUtil.CalculateDistance(start, afterFirstLeg);

        controller.Tick(TimeSpan.FromSeconds(1));
        var secondLeg = MathUtil.CalculateDistance(afterFirstLeg, npc.Transform.Local.Position);

        await Assert.That(firstLeg).IsEqualTo(WanderingSkillController.LegLength).Within(0.001f);
        await Assert.That(secondLeg).IsEqualTo(WanderingSkillController.LegLength).Within(0.001f);
        // The magnitude the wander is designed around, written out as well as compared against the
        // constant so that shrinking LegLength in production cannot shrink the expectation along with it.
        await Assert.That(firstLeg).IsEqualTo(2f).Within(0.001f);

        // Still running, and one movement packet per leg - the broadcast is what makes the controller
        // authoritative for where its owner stands.
        await Assert.That(controller.State).IsNotEqualTo(SkillController.SCState.Ended);
        await Assert.That(npc.Packets.Count).IsEqualTo(2);
    }

    [Test]
    public async Task WanderingPicksANewHeadingEveryLeg()
    {
        // The victim is not running to anywhere in particular: once it reaches the point it was heading for,
        // the next leg goes somewhere else rather than the controller ending as a leap's would.
        var npc = FearingNpc();
        var caster = new Unit { ObjId = 400, Hp = 1000, MaxHp = 1000 };
        var controller = (WanderingSkillController)SkillController.CreateSkillController(WanderingRow(60000), npc, caster);
        npc.ActiveSkillController = controller;

        controller.Tick(TimeSpan.FromSeconds(1));
        var firstLeg = npc.Transform.Local.ClonePosition();
        controller.Tick(TimeSpan.FromSeconds(1));

        var travelled = Math.Abs(npc.Transform.Local.Position.X - firstLeg.X)
                        + Math.Abs(npc.Transform.Local.Position.Y - firstLeg.Y);
        await Assert.That(travelled).IsGreaterThan(0.1f);
        await Assert.That(controller.State).IsNotEqualTo(SkillController.SCState.Ended);
    }

    [Test]
    public async Task AWanderingRowsTimeReleasesTheOwner()
    {
        var npc = FearingNpc();
        var caster = new Unit { ObjId = 400, Hp = 1000, MaxHp = 1000 };

        // A fear whose time is already up: the next tick releases the victim. Time is set rather than slept
        // through, so the test does not depend on the clock.
        var controller = (WanderingSkillController)SkillController.CreateSkillController(WanderingRow(), npc, caster);
        npc.ActiveSkillController = controller;
        controller.EndTime = DateTime.UtcNow.AddMilliseconds(-1);

        controller.Tick(TimeSpan.FromMilliseconds(100));

        await Assert.That(controller.State).IsEqualTo(SkillController.SCState.Ended);
        await Assert.That(npc.ActiveSkillController).IsNull();
    }

    [Test]
    public async Task FloatingLiftsTheOwnerAndPutsItBack()
    {
        var npc = FearingNpc();
        var caster = new Unit { ObjId = 400, Hp = 1000, MaxHp = 1000 };

        var controller = (FloatingSkillController)SkillController.CreateSkillController(FloatingRow(), npc, caster);
        npc.ActiveSkillController = controller;

        // 1.7 s of lift to 12 m: nothing at the start, half way at half time, the row's full height while it
        // is held, and nothing once the hold is over.
        await Assert.That(controller.HeightAt(0)).IsEqualTo(0f);
        await Assert.That(controller.HeightAt(850)).IsEqualTo(6f);
        await Assert.That(controller.HeightAt(1700)).IsEqualTo(12f);
        await Assert.That(controller.HeightAt(3399)).IsEqualTo(12f);
        await Assert.That(controller.HeightAt(3400)).IsEqualTo(0f);

        controller.Tick(TimeSpan.FromMilliseconds(100));
        await Assert.That(npc.Transform.Local.Position.Z).IsGreaterThanOrEqualTo(0f);

        controller.End();

        await Assert.That(npc.Transform.Local.Position.Z).IsEqualTo(0f);
        await Assert.That(npc.ActiveSkillController).IsNull();
    }

    [Test]
    public async Task APlotDrivenControllerIsCreatedOnTheCaster()
    {
        // A plot effect of type SkillController carries a leap row (the shape plot_effects uses), and the
        // effect now starts it on the caster instead of logging a line.
        var npc = FearingNpc();
        var template = new SkillControllerTemplate
        {
            Id = 1129,
            KindId = 2,
            Value = [45, 5, 2000, -25000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
        };

        template.Apply(npc, new SkillCasterUnit(npc.ObjId), npc, new SkillCastUnitTarget(npc.ObjId),
            new CastSkill(1, 1), new EffectSource(), null, DateTime.UtcNow);

        await Assert.That(npc.ActiveSkillController).IsNotNull();
        await Assert.That(npc.ActiveSkillController).IsTypeOf<LeapSkillController>();

        npc.ActiveSkillController.End();
        await Assert.That(npc.ActiveSkillController).IsNull();
    }

    [Test]
    public async Task APlotDrivenControllerForAnUncontrolledUnitIsRefused()
    {
        // The same guard the cast path uses: a Character caster may not have the server move somebody else.
        var caster = new TestPlayer { ObjId = 500, Hp = 1000, MaxHp = 1000 };
        var stranger = new RecordingNpc { ObjId = 501, Hp = 1000, MaxHp = 1000 };

        var template = new SkillControllerTemplate
        {
            Id = 1129,
            KindId = 2,
            Value = [45, 5, 2000, -25000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
        };

        // The effect acts on the caster, so this asserts the authority decision rather than a second unit:
        // a character with no world controls only itself.
        await Assert.That(SkillControllerRules.CanCreateController(caster, stranger)).IsFalse();
        await Assert.That(SkillControllerRules.CanCreateController(caster, caster)).IsTrue();
        await Assert.That(template).IsNotNull();
    }

    [Test]
    public async Task AZoneMovedNpcGetsNoServerController()
    {
        // Under zone authority a mirrored NPC is moved by the zone's movement stream; a server-driven leap
        // for the same unit would fight that stream and never arrive.
        var npc = new RecordingNpc { ObjId = 600, Hp = 1000, MaxHp = 1000, IsZoneMirror = true };

        await Assert.That(SkillControllerRules.ZoneOwnsPosition(npc, zoneAuthority: true)).IsTrue();
        await Assert.That(SkillControllerRules.CanCreateController(npc, npc, zoneAuthority: true)).IsFalse();
    }

    [Test]
    public async Task AServerMovedNpcStillGetsItsController()
    {
        var mirrorWithoutZone = new RecordingNpc { ObjId = 601, Hp = 1000, MaxHp = 1000, IsZoneMirror = true };
        var localNpc = new RecordingNpc { ObjId = 602, Hp = 1000, MaxHp = 1000 };

        await Assert.That(SkillControllerRules.CanCreateController(mirrorWithoutZone, mirrorWithoutZone, zoneAuthority: false)).IsTrue();
        await Assert.That(SkillControllerRules.CanCreateController(localNpc, localNpc, zoneAuthority: true)).IsTrue();
        await Assert.That(SkillControllerRules.ZoneOwnsPosition(localNpc, zoneAuthority: true)).IsFalse();
    }

    [Test]
    public async Task ZoneAuthorityNeverOwnsAPlayersPosition()
    {
        var player = new TestPlayer { ObjId = 603, Hp = 1000, MaxHp = 1000 };

        await Assert.That(SkillControllerRules.ZoneOwnsPosition(player, zoneAuthority: true)).IsFalse();
        await Assert.That(SkillControllerRules.CanCreateController(player, player, zoneAuthority: true)).IsTrue();
    }

    private sealed class TestPlayer : CharacterMock;
}


