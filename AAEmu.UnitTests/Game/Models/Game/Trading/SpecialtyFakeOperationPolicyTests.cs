using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.UnitTests.Game.Models.Game.Trading;

public class SpecialtyFakeOperationPolicyTests
{
    [Test]
    [Arguments(1u, 1)]
    [Arguments(31832u, 7)]
    [Arguments(uint.MaxValue, int.MaxValue)]
    public async Task Evaluate_ValidShape_IsRejectedWithoutMarketMutation(uint type, int count)
    {
        await Assert.That(SpecialtyFakeOperationPolicy.Evaluate(type, count))
            .IsEqualTo(SpecialtyFakeOperationDecision.RejectUnsupported);
    }

    [Test]
    [Arguments(0u, 1)]
    [Arguments(1u, 0)]
    [Arguments(1u, -1)]
    public async Task Evaluate_MalformedShape_IsRejectedAsMalformed(uint type, int count)
    {
        await Assert.That(SpecialtyFakeOperationPolicy.Evaluate(type, count))
            .IsEqualTo(SpecialtyFakeOperationDecision.RejectMalformed);
    }
}
