using System.Drawing;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Scripts.Commands;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

/// <summary>
/// <c>/slave bind</c> takes no parameters, so <c>SubCommandBase.PreExecute</c> routes it to the
/// <c>string[]</c> overload. Overriding only the parameter overload left the command printing its help.
/// </summary>
public class SlaveBindCommandTests
{
    private const string Help = "Seat the player on their summoned hull (Driver).";
    private const string NoSlave = "[Slave Bind] No live summoned slave to bind";

    private sealed class RecordingMessageOutput : IMessageOutput
    {
        public List<string> Recorded { get; } = [];
        public IEnumerable<string> Messages => Recorded;
        public IEnumerable<string> ErrorMessages => Recorded;
        public void SendMessage(string message) => Recorded.Add(message);
        public void SendMessage(ChatType chatType, string message, Color? color = null) => Recorded.Add(message);
        public void SendMessage(ICharacter target, string message) => Recorded.Add(message);
    }

    [Test]
    [Arguments("bind")]
    [Arguments("bind now")]
    public async Task Bind_RunsTheCommandInsteadOfPrintingHelp(string commandLine)
    {
        var owner = new Character(new UnitCustomModelParams()) { ObjId = 899 };
        var output = new RecordingMessageOutput();

        new SlaveCmd().PreExecute(owner, "slave", commandLine.Split(' '), output);

        await Assert.That(output.Recorded).Contains(NoSlave);
        await Assert.That(output.Recorded.Any(m => m.Contains(Help))).IsFalse();
    }

    [Test]
    public async Task BindHelp_StillPrintsHelp()
    {
        var owner = new Character(new UnitCustomModelParams()) { ObjId = 899 };
        var output = new RecordingMessageOutput();

        new SlaveCmd().PreExecute(owner, "slave", ["bind", "help"], output);

        await Assert.That(output.Recorded.Any(m => m.Contains(Help))).IsTrue();
        await Assert.That(output.Recorded).DoesNotContain(NoSlave);
    }
}
