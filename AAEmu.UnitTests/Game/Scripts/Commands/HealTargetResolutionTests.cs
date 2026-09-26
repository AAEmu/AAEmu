using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

public class HealTargetResolutionTests
{
    [Test]
    public async Task ResolveTarget_NoArguments_PreservesCurrentTarget()
    {
        var character = new Character(new UnitCustomModelParams());
        var target = new Npc();
        character.CurrentTarget = target;

        var (targetPlayer, playerTarget) = Heal.ResolveTarget(character, [], _ => null);

        await Assert.That(targetPlayer).IsNull();
        await Assert.That(playerTarget).IsSameReferenceAs(target);
    }

    [Test]
    public async Task ResolveTarget_ExplicitSelf_IgnoresCurrentTarget()
    {
        var character = new Character(new UnitCustomModelParams());
        character.CurrentTarget = new Npc();

        var (targetPlayer, playerTarget) = Heal.ResolveTarget(character, ["self"], _ => null);

        await Assert.That(targetPlayer).IsSameReferenceAs(character);
        await Assert.That(playerTarget).IsSameReferenceAs(character);
    }
}
