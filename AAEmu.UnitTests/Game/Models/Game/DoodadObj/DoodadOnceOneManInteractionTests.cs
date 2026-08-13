using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadOnceOneManInteractionTests
{
    [Test]
    public async Task DoFunc_AppliesOnce_RegistersAfterSuccess_BlocksRepeat()
    {
        var doodad = new Doodad
        {
            Template = new DoodadTemplate { OnceOneMan = true, FuncGroups = [] }
        };
        var character = new Character(new UnitCustomModelParams()) { Id = 42, Name = "Tester" };
        // NextPhase -1 completes without changing FuncGroupId (avoids DoodadManager in tests).
        var func = new DoodadFunc { NextPhase = -1, Count = 0 };
        var applied = 0;

        doodad.DoFuncWithApply(character, func, (_, owner) =>
        {
            applied++;
            owner.ToNextPhase = true;
        });

        await Assert.That(applied).IsEqualTo(1);
        await Assert.That(doodad.HasOnceOneManUse(character.Id)).IsTrue();

        doodad.DoFuncWithApply(character, func, (_, owner) =>
        {
            applied++;
            owner.ToNextPhase = true;
        });

        await Assert.That(applied).IsEqualTo(1);
        await Assert.That(doodad.HasOnceOneManUse(character.Id)).IsTrue();
    }

    [Test]
    public async Task DoFunc_FailedComplete_DoesNotConsumeAllowance()
    {
        var doodad = new Doodad
        {
            Template = new DoodadTemplate { OnceOneMan = true, FuncGroups = [] }
        };
        var character = new Character(new UnitCustomModelParams()) { Id = 7, Name = "Tester" };
        var func = new DoodadFunc { NextPhase = -1, Count = 0 };
        var applied = 0;

        // Func runs but does not mark success (ToNextPhase stays false).
        doodad.DoFuncWithApply(character, func, (_, _) => applied++);

        await Assert.That(applied).IsEqualTo(1);
        await Assert.That(doodad.HasOnceOneManUse(character.Id)).IsFalse();

        doodad.DoFuncWithApply(character, func, (_, owner) =>
        {
            applied++;
            owner.ToNextPhase = true;
        });

        await Assert.That(applied).IsEqualTo(2);
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
