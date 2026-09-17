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
/// Kind 4 <c>dash</c> (287 <c>skill_controllers</c> rows, 125 skills): the caster runs along its own facing.
/// The kind returned null from <c>CreateSkillController</c>, so a dash moved nobody on the server.
/// </summary>
[NotInParallel]
public class DashSkillControllerTests
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

    /// <summary>skill_controllers 7064 and its 22 siblings: 8 m over 2 s, with the negative offset they carry.</summary>
    private static SkillControllerTemplate DashRow(uint id = 7064, int value1 = 600, int value3 = 2000, int value4 = -8000) => new()
    {
        Id = id,
        KindId = 4,
        Value =
        [
            value1,   // value1 distance in thousandths when value4 is 0
            0,        // value2
            value3,   // value3 duration in ms
            value4,   // value4 distance in thousandths of a metre, signed
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        ]
    };

    private sealed class TestPlayer : CharacterMock;

    private static TestPlayer Caster(float yaw = 0f)
    {
        var player = new TestPlayer { ObjId = 100, Level = 50, Hp = 1000, MaxHp = 1000, DisabledSetPosition = true };
        player.Transform.Local.SetPosition(0f, 0f, 0f);
        player.Transform.Local.SetRotationDegree(0f, 0f, yaw);
        return player;
    }

    [Test]
    public async Task ADashMovesTheCasterAlongItsFacing()
    {
        var player = Caster();
        var target = new Unit { ObjId = 200, Hp = 1000, MaxHp = 1000 };

        var controller = SkillController.CreateSkillController(DashRow(value4: 8000), player, target);
        await Assert.That(controller).IsNotNull();
        await Assert.That(controller).IsTypeOf<DashSkillController>();

        player.ActiveSkillController = controller;
        var start = player.Transform.Local.ClonePosition();

        // Facing +X (yaw 0), a forward offset: the run goes that way and nowhere else.
        ((LinearMoveSkillController)controller).MoveTowards(4f);

        var position = player.Transform.Local.Position;
        await Assert.That(position.X - start.X).IsGreaterThan(1f);
        await Assert.That(Math.Abs(position.Y - start.Y)).IsLessThan(0.01f);
    }

    [Test]
    public async Task TheNegativeOffsetTheShippedRowsCarryWalksAgainstTheFacing()
    {
        // All 23 rows of the 8 m / 2 s family carry value4 -8000, which is a step back along the facing.
        var player = Caster();
        var target = new Unit { ObjId = 200, Hp = 1000, MaxHp = 1000 };

        var controller = SkillController.CreateSkillController(DashRow(), player, target);
        player.ActiveSkillController = controller;

        ((LinearMoveSkillController)controller).MoveTowards(4f);

        await Assert.That(player.Transform.Local.Position.X).IsLessThan(-1f);
    }

    [Test]
    public async Task ADashWithNoDistanceNeverMovesTheCaster()
    {
        // skill_controllers 4293 shape: value3 3000 with no distance at all.
        var player = Caster();
        var target = new Unit { ObjId = 200, Hp = 1000, MaxHp = 1000 };

        var controller = SkillController.CreateSkillController(DashRow(value1: 0, value3: 3000, value4: 0), player, target);
        player.ActiveSkillController = controller;

        ((LinearMoveSkillController)controller).MoveTowards(10f);

        await Assert.That(player.Transform.Local.Position.X).IsEqualTo(0f);
        await Assert.That(player.Transform.Local.Position.Y).IsEqualTo(0f);
    }

    [Test]
    public async Task ADistanceOnlyRowStillTakesTime()
    {
        // 256 of the 287 dash rows set only value1 (300..10000) and leave both value3 and value4 at 0; the
        // distance is read from it and the time comes from the nominal speed, so one step is not the whole
        // dash.
        var player = Caster();
        var target = new Unit { ObjId = 200, Hp = 1000, MaxHp = 1000 };

        var controller = (DashSkillController)SkillController.CreateSkillController(
            DashRow(id: 1919, value1: 500, value3: 0, value4: 0), player, target);
        player.ActiveSkillController = controller;

        await Assert.That(controller.Distance).IsEqualTo(0.5f);
        await Assert.That(controller.Speed).IsEqualTo(DashSkillController.NominalSpeed);

        ((LinearMoveSkillController)controller).MoveTowards(0.1f);

        await Assert.That(player.Transform.Local.Position.X).IsGreaterThan(0f);
        await Assert.That(player.Transform.Local.Position.X).IsLessThan(0.5f);
                // Still going: one step of 0.1 m does not finish a 0.5 m dash. (Execute() is what moves a controller
        // to Running, and this test drives the step directly instead of through the tick manager.)
        await Assert.That(controller.State).IsNotEqualTo(SkillController.SCState.Ended);
    }
}


