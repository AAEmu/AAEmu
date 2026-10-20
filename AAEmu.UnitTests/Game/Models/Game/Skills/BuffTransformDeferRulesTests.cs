using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffTransformDeferRulesTests
{
    [Test]
    [Arguments(27909u, false, true, true)]   // first ICD while flow is stacking → defer Abyssal Power
    [Arguments(27909u, true, true, false)]    // Abyssal already live → refresh allowed
    [Arguments(27909u, false, false, false)]  // no flow source (after transform cleared it) → allow
    [Arguments(27909u, true, false, false)]   // already live, source gone → refresh
    [Arguments(0u, false, true, false)]       // no buff id → never defer
    public async Task ShouldDefer_SoulslakeAbyssalPowerGate(
        uint incomingBuffId, bool alreadyHasIncoming, bool hasSourceBelowCeiling, bool expected)
    {
        await Assert.That(BuffTransformDeferRules.ShouldDefer(
                incomingBuffId, alreadyHasIncoming, hasSourceBelowCeiling))
            .IsEqualTo(expected);
    }
}
