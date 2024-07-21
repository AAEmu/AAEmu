using System.Collections.Generic;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Utils.Scripts.SubCommands;

public class DoodadChainSubCommandTests
{
    [Fact]
    public void PreExecute_WhenChain_ShouldCallChainSubCommand()
    {
        var mockSubCommand = new Mock<ICommandV2>();
        var mockUnitCustomModelParams = new Mock<UnitCustomModelParams>(UnitCustomModelType.None);
        var fakeCharacter = new Character(mockUnitCustomModelParams.Object);

        var command = new TestCommand(new Dictionary<ICommandV2, string[]>
        {
            {
                mockSubCommand.Object, new string[]{ "sdf"}
            }
        });

        command.PreExecute(fakeCharacter, "test", new[] { "sdf", "123" }, new CharacterMessageOutput(fakeCharacter));

        mockSubCommand.Verify(s => s.PreExecute(It.IsIn(fakeCharacter), It.IsIn("sdf"), It.Is<string[]>(a => a.Length == 1 && a[0] == "123"), It.IsAny<IMessageOutput>()));
    }

    [Fact]
    public void PreExecute_WhenChain_ShouldCallChainSubSubCommand()
    {
        var mockSubSubCommand = new Mock<ICommandV2>();
        var mockUnitCustomModelParams = new Mock<UnitCustomModelParams>(UnitCustomModelType.None);
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

        command.PreExecute(fakeCharacter, "test", new[] { "first", "second", "parameter1second", "parameter2second" }, new CharacterMessageOutput(fakeCharacter));

        mockSubSubCommand.Verify(s => s.PreExecute(It.IsIn(fakeCharacter), It.IsIn("second"), It.Is<string[]>(a => a.Length == 2 && a[0] == "parameter1second" && a[1] == "parameter2second"), It.IsAny<IMessageOutput>()));
    }

    [Fact]
    public void Execute_WhenOnlyCommand_ShouldNotThrowException()
    {
        var mockUnitCustomModelParams = new Mock<UnitCustomModelParams>(UnitCustomModelType.None);
        var fakeCharacter = new Character(mockUnitCustomModelParams.Object);

        var mockMessageOutput = new Mock<IMessageOutput>();

        var testCommand = new TestCommand(new Dictionary<ICommandV2, string[]>());
        testCommand.PreExecute(fakeCharacter, "doodad", System.Array.Empty<string>(), mockMessageOutput.Object);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Execute_WhenSendingHelp_ShouldReturnHelpText(int numberOfSupportedCommands)
    {
        var mockCharacter = new Mock<ICharacter>();
        var supportedCommands = new Dictionary<ICommandV2, string[]>();
        var mockSubCommands = new List<Mock<ICommandV2>>();
        var expectedCommands = new List<string>();
        for (var i = 0; i < numberOfSupportedCommands; i++)
        {
            var mockSubCommand = new Mock<ICommandV2>();
            mockSubCommands.Add(mockSubCommand);

            supportedCommands.Add(mockSubCommand.Object, new string[] { $"command{i}" });
            expectedCommands.Add($"command{i}");
        }

        var testCommand = new TestCommand(supportedCommands);
        // var testCommandPrefix = "Prefix";
        testCommand.PreExecute(mockCharacter.Object, "test", new string[] { "help" }, new CharacterMessageOutput(mockCharacter.Object));

        // TODO: Fix these tests
        // mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsIn($"{testCommandPrefix} {testCommand.Description}"), It.IsIn(Color.LawnGreen)), Times.Once);
        // mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.Is<string>(s => s.Contains($"{string.Join("||", expectedCommands)}")), It.IsIn(Color.LawnGreen)), Times.Once);
        // mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.Is<string>(s => s.Contains("For more details use")), It.IsIn(Color.LawnGreen)), Times.Once);
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
