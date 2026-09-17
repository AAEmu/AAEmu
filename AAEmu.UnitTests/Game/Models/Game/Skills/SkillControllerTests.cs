using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The leap controller for a player caster: it drives the caster's own server-side position, which it did not
/// do before this batch (a controller was only ever created when the caster was an NPC), and detaches from
/// the owner when it ends.
/// </summary>
/// <remarks>
/// skill_controllers 1129 (kind 2 <c>leap</c>, value3 2000 ms, value4 -25000) is the shipped shape: the row
/// asks for a 25 m leap backwards over two seconds, and the server travels at the distance over that time.
/// </remarks>
[NotInParallel]
public class SkillControllerTests
{
    private SingletonScope<WorldManager> _worldManager;
    private SingletonScope<SusManager> _susManager;
    private SingletonScope<SkillManager> _skillManager;

    /// <summary>
    /// The movement path reads all three singletons: the controller asks <see cref="SkillManager"/> for the
    /// buffs of the shackle and snare tags, <c>DisabledSetPosition</c> resets the player's delta-movement
    /// watchdog, and <c>CheckMovedPosition</c> hands the moved unit to the world's visibility index. None of
    /// them is what this class is testing, so all three are installed empty for the length of a test.
    /// </summary>
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

    /// <summary>skill_controllers 1129: leap, 2s, 25 m.</summary>
    private static SkillControllerTemplate LeapRow(uint id = 1129, int value4 = -25000) => new()
    {
        Id = id,
        KindId = 2,
        Value =
        [
            45,       // value1 angle, used by the client animation
            5,        // value2 speed rating, the client's own animation speed
            2000,     // value3 duration in ms
            value4,   // value4 distance offset in thousandths of a metre
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        ]
    };

    private sealed class TestPlayer : CharacterMock;

    private static (TestPlayer Player, Unit Target) CreateCast()
    {
        var player = new TestPlayer { ObjId = 100, Level = 50, Hp = 1000, MaxHp = 1000, DisabledSetPosition = true };
        player.Transform.Local.SetPosition(0f, 0f, 0f);

        var target = new Unit { ObjId = 200, Hp = 1000, MaxHp = 1000 };
        target.Transform.Local.SetPosition(20f, 0f, 0f);

        return (player, target);
    }

    [Test]
    public async Task APlayerLeapMovesTheServerSidePosition()
    {
        var (player, target) = CreateCast();

        var controller = SkillController.CreateSkillController(LeapRow(), player, target);
        await Assert.That(controller).IsNotNull();

        player.ActiveSkillController = controller;
        var start = player.Transform.Local.ClonePosition();

        // One second of the two the row asks for, at the speed the constructor derived from the distance.
        ((LinearMoveSkillController)controller).MoveTowards(12.5f);

        var position = player.Transform.Local.Position;
        await Assert.That(Math.Abs(position.X - start.X)).IsGreaterThan(1f);
    }

    [Test]
    public async Task EndingALeapDetachesItFromTheOwner()
    {
        var (player, target) = CreateCast();

        var controller = SkillController.CreateSkillController(LeapRow(), player, target);
        player.ActiveSkillController = controller;

        controller.End();

        // Npc, Mate and the route simulation all gate on ActiveSkillController: a finished controller must not
        // keep a unit that is standing still looking like one under a controller.
        await Assert.That(player.ActiveSkillController).IsNull();
        await Assert.That(controller.State).IsEqualTo(SkillController.SCState.Ended);
    }

    [Test]
    public async Task ALeapAtTheEndPositionEndsOnTheFirstStep()
    {
        var (player, target) = CreateCast();

        // Owner and target at the same spot and no distance offset: the end position is where the owner
        // already is, so the first step ends the controller.
        target.Transform.Local.SetPosition(0f, 0f, 0f);
        var controller = SkillController.CreateSkillController(LeapRow(value4: 0), player, target);
        player.ActiveSkillController = controller;

        ((LinearMoveSkillController)controller).MoveTowards(5f);

        await Assert.That(controller.State).IsEqualTo(SkillController.SCState.Ended);
        await Assert.That(player.ActiveSkillController).IsNull();
    }

    [Test]
    public async Task AnNpcLeapStillWorks()
    {
        // The path that already existed: an NPC leap is created and moved exactly as before.
        var npc = new TestNpc { ObjId = 300, Level = 50, Hp = 1000, MaxHp = 1000, DisabledSetPosition = true };
        npc.Transform.Local.SetPosition(0f, 0f, 0f);
        var target = new Unit { ObjId = 301, Hp = 1000, MaxHp = 1000 };
        target.Transform.Local.SetPosition(15f, 0f, 0f);

        var controller = SkillController.CreateSkillController(LeapRow(), npc, target);

        await Assert.That(controller).IsNotNull();
        await Assert.That(controller.Owner).IsSameReferenceAs(npc);

        ((LinearMoveSkillController)controller).MoveTowards(10f);
        await Assert.That(Math.Abs(npc.Transform.Local.Position.X) > 1f).IsTrue();
    }

    [Test]
    public async Task TheControllerKindsCoverTheContentTable()
    {
        // enum_skill_controller_kinds: 0-9 and 11, with no 10. A kind the enum does not name would be
        // indistinguishable from an unknown row in the log. The enum is internal to AAEmu.Game, so the table
        // it mirrors is read off the assembly.
        var kindType = typeof(SkillController).Assembly
            .GetType("AAEmu.Game.Models.Game.Skills.SkillControllers.SkillControllerKind");
        await Assert.That(kindType).IsNotNull();

        var kinds = Enum.GetValues(kindType).Cast<int>().ToList();

        await Assert.That(kinds.Count).IsEqualTo(11);
        await Assert.That(kinds.Contains(9)).IsTrue();
        await Assert.That(kinds.Contains(11)).IsTrue();
        await Assert.That(kinds.Contains(10)).IsFalse();
    }

    private sealed class TestNpc : AAEmu.Game.Models.Game.NPChar.Npc
    {
        public override void BroadcastPacket(AAEmu.Game.Core.Network.Game.GamePacket packet, bool self) { }
    }
}

