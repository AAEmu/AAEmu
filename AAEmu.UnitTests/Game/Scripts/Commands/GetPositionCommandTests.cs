using System.Drawing;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Scripts.Commands;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

/// <summary>
/// <c>/pos</c> against a targeted npc. An instance or event npc is a zone mirror with no spawner, and
/// the command used to dereference it and throw — the raw exception was then written into the player's
/// chat by the command dispatcher, every time the client asked for its position.
/// </summary>
public class GetPositionCommandTests
{
    private sealed class CaptureOutput : IMessageOutput
    {
        public List<string> Sent { get; } = [];
        public IEnumerable<string> Messages => Sent;
        public IEnumerable<string> ErrorMessages => Sent;

        public void SendMessage(string message) => Sent.Add(message);
        public void SendMessage(ChatType chatType, string message, Color? color = null) => Sent.Add(message);
        public void SendMessage(ICharacter target, string message) => Sent.Add(message);
    }

    [Test]
    public async Task TargetNpc_WithoutSpawner_ReportsAbsentIdInsteadOfThrowing()
    {
        var character = new Character(new UnitCustomModelParams());
        var npc = new Npc { TemplateId = 19332 };
        character.CurrentTarget = npc;
        var output = new CaptureOutput();

        // A zone-mirror npc has Spawner == null; this must not throw.
        new GetPosition().Execute(character, [], output);

        await Assert.That(output.Sent.Count).IsEqualTo(1);
        await Assert.That(output.Sent[0].Contains("Id: -")).IsTrue();
        await Assert.That(output.Sent[0].Contains("TemplateId: 19332")).IsTrue();
    }

    [Test]
    public async Task TargetNpc_WithSpawner_ReportsItsId()
    {
        var character = new Character(new UnitCustomModelParams());
        var npc = new Npc { TemplateId = 19139, Spawner = new NpcSpawner { Id = 777 } };
        character.CurrentTarget = npc;
        var output = new CaptureOutput();

        new GetPosition().Execute(character, [], output);

        await Assert.That(output.Sent.Count).IsEqualTo(1);
        await Assert.That(output.Sent[0].Contains("Id: 777")).IsTrue();
    }
}
