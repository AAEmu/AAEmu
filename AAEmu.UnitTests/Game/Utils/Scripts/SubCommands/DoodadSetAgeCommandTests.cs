using System.Drawing;

using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Scripts.Commands;
using AAEmu.Game.Utils.Scripts;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Utils.Scripts.SubCommands;

public class DoodadSetAgeCommandTests
{
    [Test]
    [Arguments("age", 3600u)]
    [Arguments("setage", 86_400u)]
    [Arguments("setage", 0u)]
    public async Task PreExecute_ValidAge_UpdatesPlantTimeInSecondsAndRefreshesVisibility(string alias, uint ageSeconds)
    {
        var character = CreateCharacter();
        var world = character.ParentWorld;
        var doodad = new RecordingDoodad
        {
            ObjId = 123,
            PlantTime = DateTime.UtcNow.AddDays(-7),
            GrowthTime = DateTime.UtcNow.AddHours(1)
        };
        var growthTime = doodad.GrowthTime;
        world.AddObject(doodad);
        var command = new DoodadCmd();
        var output = Mock.Of<IMessageOutput>();
        var before = DateTime.UtcNow;

        command.PreExecute(character, "doodad", [alias, "123", ageSeconds.ToString()], output.Object);

        var after = DateTime.UtcNow;
        await Assert.That(doodad.PlantTime).IsGreaterThanOrEqualTo(before.AddSeconds(-ageSeconds));
        await Assert.That(doodad.PlantTime).IsLessThanOrEqualTo(after.AddSeconds(-ageSeconds));
        await Assert.That(string.Join(", ", doodad.VisibilityChanges)).IsEqualTo("Hide, Show");
        await Assert.That(doodad.PlantTimeWhenHidden).IsEqualTo(doodad.PlantTime);
        await Assert.That(doodad.GrowthTime).IsEqualTo(growthTime);
        output.SendMessage(Is<string>(message => message.Contains("123"))).WasCalled(Times.Once);
    }

    [Test]
    public void PreExecute_UnknownObject_ReportsMissingDoodad()
    {
        var character = CreateCharacter();
        var output = Mock.Of<IMessageOutput>();
        var command = new DoodadCmd();

        command.PreExecute(character, "doodad", ["setage", "999", "3600"], output.Object);

        output.SendMessage(ChatType.System, Is<string>(message => message.Contains("999")), Color.Red)
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task PreExecute_NegativeAge_DoesNotChangeDoodad()
    {
        var character = CreateCharacter();
        var world = character.ParentWorld;
        var plantTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var doodad = new RecordingDoodad { ObjId = 123, PlantTime = plantTime };
        world.AddObject(doodad);
        var output = Mock.Of<IMessageOutput>();
        var command = new DoodadCmd();

        command.PreExecute(character, "doodad", ["setage", "123", "-1"], output.Object);

        await Assert.That(doodad.PlantTime).IsEqualTo(plantTime);
        await Assert.That(doodad.VisibilityChanges).IsEmpty();
        output.SendMessage(ChatType.System, Any<string>(), Color.Red).WasCalled(Times.Once);
    }

    private static CharacterMock CreateCharacter()
    {
        var character = new CharacterMock();
        var world = new WorldInstance(new WorldTemplate(), 1, true, character.Transform.InstanceId);
        // This world has no runtime services for its finalizer to shut down.
        GC.SuppressFinalize(world);
        character.ParentWorld = world;
        return character;
    }

    private sealed class RecordingDoodad : Doodad
    {
        public List<string> VisibilityChanges { get; } = [];
        public DateTime PlantTimeWhenHidden { get; private set; }

        public override void Hide()
        {
            PlantTimeWhenHidden = PlantTime;
            VisibilityChanges.Add("Hide");
        }

        public override void Show()
        {
            VisibilityChanges.Add("Show");
        }
    }
}
