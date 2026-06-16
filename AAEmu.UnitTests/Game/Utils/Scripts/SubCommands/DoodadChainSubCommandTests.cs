using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
namespace AAEmu.UnitTests.Game.Utils.Scripts.SubCommands;

public class DoodadChainSubCommandTests
{
    [Test]
    public void PreExecute_WhenChain_ShouldCallChainSubCommand()
    {
        var mockSubCommand = Mock.Of<ICommandV2>();
        var mockUnitCustomModelParams = Mock.Of<UnitCustomModelParams>(UnitCustomModelType.None);
        var fakeCharacter = new Character(mockUnitCustomModelParams.Object);

        var command = new TestCommand(new Dictionary<ICommandV2, string[]>
        {
            {
                mockSubCommand.Object, new string[]{ "sdf"}
            }
        });

        command.PreExecute(fakeCharacter, "test", ["sdf", "123"], new CharacterMessageOutput(fakeCharacter));

        mockSubCommand.PreExecute(fakeCharacter, "sdf", Is<string[]>(a => a.Length == 1 && a[0] == "123"), Any<IMessageOutput>()).WasCalled();
    }

    [Test]
    public void PreExecute_WhenChain_ShouldCallChainSubSubCommand()
    {
        var mockSubSubCommand = Mock.Of<ICommandV2>();
        var mockUnitCustomModelParams = Mock.Of<UnitCustomModelParams>(UnitCustomModelType.None);
        var fakeCharacter = new Character(mockUnitCustomModelParams.Object);

        var subCommand = new SubTestCommand(new Dictionary<ICommandV2, string[]>
        {
            {
                mockSubSubCommand.Object, new string[]{ "second"}
            }
        });

        var command = new TestCommand(new Dictionary<ICommandV2, string[]>
        {
            {
                subCommand, new string[]{ "first"}
            }
        });

        command.PreExecute(fakeCharacter, "test", ["first", "second", "parameter1second", "parameter2second"], new CharacterMessageOutput(fakeCharacter));

        mockSubSubCommand.PreExecute(fakeCharacter, "second", Is<string[]>(a => a.Length == 2 && a[0] == "parameter1second" && a[1] == "parameter2second"), Any<IMessageOutput>()).WasCalled();
    }

    [Test]
    public void Execute_WhenOnlyCommand_ShouldNotThrowException()
    {
        var mockUnitCustomModelParams = Mock.Of<UnitCustomModelParams>(UnitCustomModelType.None);
        var fakeCharacter = new Character(mockUnitCustomModelParams.Object);

        var mockMessageOutput = Mock.Of<IMessageOutput>();

        var testCommand = new TestCommand([]);
        testCommand.PreExecute(fakeCharacter, "doodad", System.Array.Empty<string>(), mockMessageOutput.Object);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public void Execute_WhenSendingHelp_ShouldReturnHelpText(int numberOfSupportedCommands)
    {
        var mockCharacter = Mock.Of<ICharacter>();
        var supportedCommands = new Dictionary<ICommandV2, string[]>();
        var mockSubCommands = new List<Mock<ICommandV2>>();
        var expectedCommands = new List<string>();
        for (var i = 0; i < numberOfSupportedCommands; i++)
        {
            var mockSubCommand = Mock.Of<ICommandV2>();
            mockSubCommands.Add(mockSubCommand);

            supportedCommands.Add(mockSubCommand.Object, [$"command{i}"]);
            expectedCommands.Add($"command{i}");
        }

        var testCommand = new TestCommand(supportedCommands);
        // var testCommandPrefix = "Prefix";
        testCommand.PreExecute(mockCharacter.Object, "test", ["help"], new CharacterMessageOutput(mockCharacter.Object));

        // TODO: Fix these tests
        // mockCharacter.SendMessage(Any<ChatType>(), $"{testCommandPrefix} {testCommand.Description}", Color.LawnGreen).WasCalled(Times.Once);
        // mockCharacter.SendMessage(Any<ChatType>(), Is<string>(s => s.Contains($"{string.Join("||", expectedCommands)}")), Color.LawnGreen).WasCalled(Times.Once);
        // mockCharacter.SendMessage(Any<ChatType>(), Is<string>(s => s.Contains("For more details use")), Color.LawnGreen).WasCalled(Times.Once);
    }

    public class TestCommand : SubCommandBase
    {
        public TestCommand(Dictionary<ICommandV2, string[]> register) : base(register)
        {
            Title = "Prefix";
            Description = "Mock Command";
            CallPrefix = "Help Message";
        }
    }

    public class SubTestCommand : SubCommandBase
    {
        public SubTestCommand(Dictionary<ICommandV2, string[]> register) : base(register) { }
    }
}
