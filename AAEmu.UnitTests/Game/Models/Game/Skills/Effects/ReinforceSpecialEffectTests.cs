using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// The reinforcement steps a player triggers from the artifact window are resolved by name from the enum, so
/// these two pin that an action class exists for them. Without one the server logs "unknown special effect" on
/// every click and the step is a silent no-op, which is what made the client's Confirm button look broken.
/// </summary>
public class ReinforceSpecialEffectTests
{
    [Test]
    public async Task TheReinforceSteps_AreRecognisedEffects()
    {
        await Assert.That(SpecialEffect.IsImplemented(SpecialType.EquipSlotReinforceAddExp)).IsTrue();
        await Assert.That(SpecialEffect.IsImplemented(SpecialType.EquipSlotReinforceChangeLevelEffect)).IsTrue();
    }

    [Test]
    public async Task IsImplemented_CanAnswerNo()
    {
        // guards the test above from being vacuous without asserting on a type that happens to have an action:
        // a value that is not a declared type cannot resolve to a class.
        await Assert.That(SpecialEffect.IsImplemented((SpecialType)(-1))).IsFalse();
    }
}
