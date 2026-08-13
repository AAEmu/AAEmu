using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadOnceOneManInteractionTests
{
    [Test]
    public async Task Authorize_AllowsFirstUse_ThenBlocksBeforeSideEffect()
    {
        var doodad = new Doodad
        {
            Template = new DoodadTemplate { OnceOneMan = true }
        };
        var character = new Character(new UnitCustomModelParams()) { Id = 42, Name = "Tester" };

        await Assert.That(doodad.TryAuthorizeOnceOneManInteraction(character, out var blocked)).IsTrue();
        await Assert.That(blocked).IsNull();

        // Successful complete records the character (DoFunc → CompleteFunc order).
        await Assert.That(doodad.TryRegisterOnceOneMan(character.Id)).IsTrue();

        await Assert.That(doodad.TryAuthorizeOnceOneManInteraction(character, out blocked)).IsFalse();
        await Assert.That(blocked).IsEqualTo(character);
        // Second authorization failure happens before any Func.Use side effect.
        await Assert.That(doodad.HasOnceOneManUse(character.Id)).IsTrue();
    }

    [Test]
    public async Task Authorize_IgnoredWhenOnceOneManOff()
    {
        var doodad = new Doodad
        {
            Template = new DoodadTemplate { OnceOneMan = false }
        };
        var character = new Character(new UnitCustomModelParams()) { Id = 7 };
        doodad.TryRegisterOnceOneMan(character.Id);

        await Assert.That(doodad.TryAuthorizeOnceOneManInteraction(character, out _)).IsTrue();
    }
}
